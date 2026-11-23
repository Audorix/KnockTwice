using System.Security.Cryptography;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Contacts;
using Audorix.ChatX.Application.Conversations;
using Audorix.ChatX.Domain.Attachments;
using Audorix.ChatX.Domain.Conversations;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Attachments;

/// <summary>An attachment as shown with its message.</summary>
public sealed record AttachmentView(Guid Id, string FileName, string ContentType, FileKind Kind, long Size);

/// <summary>An attachment with its stored content, for download.</summary>
public sealed record AttachmentContent(Attachment Attachment, Stream Content);

/// <summary>
/// Attachment use cases (Phase 7, owner decisions of 2026-11-22): allow-listed types whose content
/// signature must match, 25 MB per file, 10 per message and 1 GB per person by default (all
/// configurable); optional malware scanning (off by default) keeps files quarantined until clean
/// and while the scanner is unavailable; uploads never sent are removed after 24 hours; deleting a
/// message deletes its files; access always follows the conversation (people who leave lose it).
/// <para>
/// Lifecycle (Architecture §17.5): the record is committed as <c>PendingUpload</c> before the bytes
/// go to storage, so a crash in between leaves a record the cleanup job finds; storage and database
/// share no transaction, so removals of content are tracked and retried.
/// </para>
/// </summary>
public sealed class AttachmentService(
    IAttachmentStore attachments,
    IAttachmentStorage storage,
    IMalwareScanner scanner,
    IConversationStore conversationStore,
    ConversationService conversations,
    IContactStore contacts,
    TimeProvider time,
    IOptionsSnapshot<AttachmentSettings> settings)
{
    private const string NotFound = "attachment_not_found";

    /// <summary>A random key with no relation to the user's file name: <c>yyyy/MM/{32 hex}</c>.</summary>
    public static string NewStorageKey(DateTimeOffset nowUtc) =>
        $"{nowUtc:yyyy}/{nowUtc:MM}/{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";

    /// <summary>
    /// Uploads a file into a conversation the caller takes part in. Returns the attachment, which is
    /// ready unless scanning is enabled and the scanner is unavailable (then it stays quarantined).
    /// </summary>
    public async Task<Attachment> UploadAsync(
        Guid userId, Guid conversationId, string? fileName, string? declaredContentType, long? declaredLength, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var options = settings.Value;
        if (!options.Enabled)
        {
            throw new FeatureDisabledException("attachments_disabled");
        }

        var (conversation, _) = await conversations.RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        await RequireNotBlockedAsync(userId, conversation, cancellationToken).ConfigureAwait(false);

        var name = Attachment.NormalizeFileName(fileName);
        var type = FileTypes.RequireAllowed(name, options.EffectiveExtensions);
        FileTypes.RequireDeclaredMatches(type, declaredContentType);
        if (declaredLength > options.MaxFileSize)
        {
            throw new PayloadTooLargeException("attachment_too_large");
        }

        var used = options.UserQuota > 0 ? await attachments.SumStoredSizeAsync(userId, cancellationToken).ConfigureAwait(false) : 0;
        if (options.UserQuota > 0 && used + (declaredLength ?? 0) > options.UserQuota)
        {
            throw new PayloadTooLargeException("attachment_quota_exceeded");
        }

        await using var buffer = await BufferAsync(content, options.MaxFileSize, cancellationToken).ConfigureAwait(false);
        FileTypes.RequireSignature(type, buffer.Stream);
        if (options.UserQuota > 0 && used + buffer.Length > options.UserQuota)
        {
            throw new PayloadTooLargeException("attachment_quota_exceeded");
        }

        // 1. Commit the intent, so a crash after writing the bytes leaves something to clean up.
        var now = time.GetUtcNow();
        var attachment = Attachment.Begin(Guid.CreateVersion7(now), conversationId, userId, NewStorageKey(now), name, now);
        attachments.Add(attachment);
        await attachments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 2. Write the bytes under the generated key.
        try
        {
            buffer.Stream.Position = 0;
            await storage.PutAsync(attachment.StorageKey, buffer.Stream, buffer.Length, type.ContentType, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            attachment.Delete(time.GetUtcNow());
            await TryRemoveContentAsync(attachment, CancellationToken.None).ConfigureAwait(false);
            await attachments.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw ex as StorageUnavailableException ?? new StorageUnavailableException("The attachment could not be stored.", ex);
        }

        attachment.Stored(buffer.Length, type.ContentType, buffer.Sha256);

        // 3. Scan when configured; files stay quarantined until a clean verdict.
        await ScanAsync(attachment, buffer.Stream, cancellationToken).ConfigureAwait(false);
        await attachments.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        if (attachment.Status == AttachmentStatus.Rejected)
        {
            throw new RequestValidationException("file", attachment.RejectionCode ?? "attachment_rejected");
        }

        return attachment;
    }

    /// <summary>Metadata of an attachment the caller may see (their own uploads in any state; others' only when ready and visible).</summary>
    public async Task<Attachment> GetAsync(Guid userId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await attachments.FindAsync(attachmentId, cancellationToken).ConfigureAwait(false);
        if (attachment is null || attachment.Status == AttachmentStatus.Deleted)
        {
            throw new ResourceNotFoundException(NotFound);
        }

        if (attachment.OwnerUserId == userId && !attachment.IsLinked)
        {
            await RequireConversationAccessAsync(userId, attachment.ConversationId, cancellationToken).ConfigureAwait(false);
            return attachment;
        }

        return await RequireReadableAsync(userId, attachment, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens a ready attachment the caller may read. Authorized on every request through its conversation and message.</summary>
    public async Task<AttachmentContent> OpenAsync(Guid userId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await attachments.FindAsync(attachmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException(NotFound);
        await RequireReadableAsync(userId, attachment, cancellationToken).ConfigureAwait(false);
        var stream = await storage.OpenReadAsync(attachment.StorageKey, cancellationToken).ConfigureAwait(false);

        // A missing object must not look like an available file.
        return stream is null ? throw new ResourceNotFoundException(NotFound) : new AttachmentContent(attachment, stream);
    }

    /// <summary>Same checks as <see cref="OpenAsync"/> without reading the content (conditional requests).</summary>
    public async Task<Attachment> AuthorizeReadAsync(Guid userId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await attachments.FindAsync(attachmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException(NotFound);
        return await RequireReadableAsync(userId, attachment, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The owner removes an upload that was not sent. Sent files go with their message.</summary>
    public async Task DeleteAsync(Guid userId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await attachments.FindAsync(attachmentId, cancellationToken).ConfigureAwait(false);
        if (attachment is null || attachment.OwnerUserId != userId || attachment.Status == AttachmentStatus.Deleted)
        {
            throw new ResourceNotFoundException(NotFound);
        }

        if (attachment.IsLinked)
        {
            throw new ConflictException("attachment_in_use");
        }

        attachment.Delete(time.GetUtcNow());
        await attachments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RemoveContentAndSaveAsync([attachment], cancellationToken).ConfigureAwait(false);
    }

    // ---- Used by messaging -------------------------------------------------------------------

    /// <summary>
    /// Checks and links attachments to a new message (saved with it by the caller). Each must be a
    /// ready, unsent upload of the sender in the same conversation.
    /// </summary>
    public async Task LinkAsync(Guid userId, ConversationEvent message, IReadOnlyCollection<Guid> attachmentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        foreach (var attachment in await RequireLinkableAsync(userId, message.ConversationId, attachmentIds, cancellationToken).ConfigureAwait(false))
        {
            attachment.Link(message.ConversationId, userId, message.Id);
        }
    }

    /// <summary>
    /// Copies the ready files of a message into another conversation as the caller's own unsent
    /// uploads, for forwarding (Phase 18). The copies count towards the caller's quota; the bytes
    /// were already checked (and scanned) as the originals, so the copies are ready at once.
    /// Copies that are never sent are removed by the cleanup job like any unsent upload.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> CopyForForwardAsync(Guid userId, Guid sourceMessageId, Guid targetConversationId, CancellationToken cancellationToken)
    {
        var originals = (await attachments.ListForMessageAsync(sourceMessageId, cancellationToken).ConfigureAwait(false)).Where(a => a.IsReady).ToList();
        if (originals.Count == 0)
        {
            return [];
        }

        var options = settings.Value;
        if (!options.Enabled)
        {
            throw new FeatureDisabledException("attachments_disabled");
        }

        if (options.UserQuota > 0
            && await attachments.SumStoredSizeAsync(userId, cancellationToken).ConfigureAwait(false) + originals.Sum(a => a.Size) > options.UserQuota)
        {
            throw new PayloadTooLargeException("attachment_quota_exceeded");
        }

        var copies = new List<Guid>();
        foreach (var original in originals.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id))
        {
            var now = time.GetUtcNow();
            var copy = Attachment.Begin(Guid.CreateVersion7(now), targetConversationId, userId, NewStorageKey(now), original.FileName, now);
            attachments.Add(copy);
            await attachments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var source = await storage.OpenReadAsync(original.StorageKey, cancellationToken).ConfigureAwait(false)
                    ?? throw new ResourceNotFoundException(NotFound);
                await using (source.ConfigureAwait(false))
                {
                    await storage.PutAsync(copy.StorageKey, source, original.Size, original.ContentType, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                copy.Delete(time.GetUtcNow());
                await TryRemoveContentAsync(copy, CancellationToken.None).ConfigureAwait(false);
                await attachments.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                if (ex is ApplicationRuleException or StorageUnavailableException)
                {
                    throw;
                }

                throw new StorageUnavailableException("The attachment could not be copied.", ex);
            }

            copy.Stored(original.Size, original.ContentType, original.Sha256);
            copy.MarkReady(time.GetUtcNow());
            await attachments.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            copies.Add(copy.Id);
        }

        return copies;
    }

    /// <summary>Validates ids for a send without changing anything (count, duplicates, ownership, readiness).</summary>
    public async Task<IReadOnlyList<Attachment>> RequireLinkableAsync(
        Guid userId, Guid conversationId, IReadOnlyCollection<Guid> attachmentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachmentIds);
        if (attachmentIds.Count > settings.Value.MaxPerMessage)
        {
            throw new RequestValidationException("attachmentIds", "too_many_attachments");
        }

        if (attachmentIds.Distinct().Count() != attachmentIds.Count)
        {
            throw new RequestValidationException("attachmentIds", "attachment_duplicate");
        }

        var found = await attachments.FindManyAsync(attachmentIds, cancellationToken).ConfigureAwait(false);
        if (found.Count != attachmentIds.Count
            || found.Any(a => a.OwnerUserId != userId || a.ConversationId != conversationId || a.IsLinked || a.Status == AttachmentStatus.Deleted))
        {
            // One code for every reason: other people's uploads are not revealed.
            throw new RequestValidationException("attachmentIds", "attachment_not_available");
        }

        if (found.Any(a => !a.IsReady))
        {
            throw new RequestValidationException("attachmentIds", "attachment_not_ready");
        }

        return found;
    }

    /// <summary>Marks the attachments of a deleted message as deleted (saved with the message by the caller).</summary>
    public async Task<IReadOnlyList<Attachment>> DeleteForMessageAsync(Guid messageEntryId, CancellationToken cancellationToken)
    {
        var linked = await attachments.ListForMessageAsync(messageEntryId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        foreach (var attachment in linked)
        {
            attachment.Delete(now);
        }

        return linked;
    }

    /// <summary>Removes stored content after the deletion was committed; failures are retried by the cleanup job.</summary>
    public async Task RemoveContentAndSaveAsync(IReadOnlyCollection<Attachment> deleted, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var attachment in deleted.Where(a => !a.ContentRemoved))
        {
            changed |= await TryRemoveContentAsync(attachment, cancellationToken).ConfigureAwait(false);
        }

        if (changed)
        {
            await attachments.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>How an attachment is shown with its message (history and message events).</summary>
    public static AttachmentView ToView(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return new(attachment.Id, attachment.FileName, attachment.ContentType, FileTypes.KindOf(attachment.ContentType), attachment.Size);
    }

    // ---- Cleanup -----------------------------------------------------------------------------

    public sealed record CleanupResult(int Abandoned, int ContentRemoved, int Rescanned);

    /// <summary>
    /// Idempotent cleanup: deletes abandoned and unfinished uploads, retries content removal and
    /// rescans quarantined files when a scanner is configured.
    /// </summary>
    public async Task<CleanupResult> CleanUpAsync(int limit, CancellationToken cancellationToken)
    {
        var options = settings.Value;
        var now = time.GetUtcNow();
        var abandoned = await attachments.ListAbandonedAsync(now - options.AbandonedAfter, now - options.PendingTimeout, limit, cancellationToken).ConfigureAwait(false);
        foreach (var attachment in abandoned)
        {
            attachment.Delete(now);
        }

        if (abandoned.Count > 0)
        {
            await attachments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var removed = 0;
        foreach (var attachment in await attachments.ListContentToRemoveAsync(limit, cancellationToken).ConfigureAwait(false))
        {
            if (await TryRemoveContentAsync(attachment, cancellationToken).ConfigureAwait(false))
            {
                removed++;
            }
        }

        var rescanned = 0;
        if (scanner.Enabled)
        {
            foreach (var attachment in await attachments.ListQuarantinedAsync(limit, cancellationToken).ConfigureAwait(false))
            {
                var stream = await TryOpenAsync(attachment, cancellationToken).ConfigureAwait(false);
                if (stream is null)
                {
                    continue;
                }

                await using (stream.ConfigureAwait(false))
                {
                    await ScanAsync(attachment, stream, cancellationToken).ConfigureAwait(false);
                }

                if (attachment.Status != AttachmentStatus.Quarantined)
                {
                    rescanned++;
                }
            }
        }

        await attachments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CleanupResult(abandoned.Count, removed, rescanned);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private async Task ScanAsync(Attachment attachment, Stream content, CancellationToken cancellationToken)
    {
        if (!scanner.Enabled)
        {
            attachment.MarkReady(time.GetUtcNow());
            return;
        }

        ScanVerdict verdict;
        try
        {
            content.Position = content.CanSeek ? 0 : content.Position;
            verdict = await scanner.ScanAsync(content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            verdict = ScanVerdict.Unavailable;
        }

        switch (verdict)
        {
            case ScanVerdict.Clean:
                attachment.MarkReady(time.GetUtcNow());
                break;
            case ScanVerdict.Infected:
                attachment.Reject("attachment_infected", time.GetUtcNow());
                await TryRemoveContentAsync(attachment, CancellationToken.None).ConfigureAwait(false);
                break;
            default:
                break; // Stays quarantined; the cleanup job scans again.
        }
    }

    private async Task<Stream?> TryOpenAsync(Attachment attachment, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await storage.OpenReadAsync(attachment.StorageKey, cancellationToken).ConfigureAwait(false);
            if (stream is null || stream.CanSeek)
            {
                return stream;
            }

            // The scanner may need to read from the start more than once.
            var copy = new MemoryStream();
            await using (stream.ConfigureAwait(false))
            {
                await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
            }

            copy.Position = 0;
            return copy;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Removes the stored bytes; true when they are gone (now or already).</summary>
    private async Task<bool> TryRemoveContentAsync(Attachment attachment, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(attachment.StorageKey, cancellationToken).ConfigureAwait(false);
            attachment.ContentWasRemoved();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false; // Retried by the cleanup job.
        }
    }

    /// <summary>
    /// Others' attachments: ready, linked to a message that is not deleted and inside the caller's
    /// visible history. The caller's own unsent upload: ready. Everything else is "not found".
    /// </summary>
    private async Task<Attachment> RequireReadableAsync(Guid userId, Attachment attachment, CancellationToken cancellationToken)
    {
        if (!attachment.IsReady)
        {
            throw new ResourceNotFoundException(NotFound);
        }

        var me = await RequireConversationAccessAsync(userId, attachment.ConversationId, cancellationToken).ConfigureAwait(false);
        if (attachment.MessageEntryId is not { } entryId)
        {
            return attachment.OwnerUserId == userId ? attachment : throw new ResourceNotFoundException(NotFound);
        }

        var entry = await conversationStore.FindEntryAsync(attachment.ConversationId, entryId, cancellationToken).ConfigureAwait(false);
        return entry is { IsMessage: true, IsDeleted: false } && entry.Sequence >= me.VisibleFromSequence
            ? attachment
            : throw new ResourceNotFoundException(NotFound);
    }

    private async Task<ConversationParticipant> RequireConversationAccessAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        try
        {
            return (await conversations.RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false)).Me;
        }
        catch (ResourceNotFoundException)
        {
            throw new ResourceNotFoundException(NotFound);
        }
    }

    private async Task RequireNotBlockedAsync(Guid userId, Conversation conversation, CancellationToken cancellationToken)
    {
        if (conversation.Type != ConversationType.Direct)
        {
            return;
        }

        var others = (await conversationStore.ListActiveParticipantsAsync(conversation.Id, cancellationToken).ConfigureAwait(false)).Where(p => p.UserId != userId);
        var blocks = await contacts.ListBlockPartnersAsync(userId, cancellationToken).ConfigureAwait(false);
        if (others.Any(o => blocks.Contains(o.UserId)))
        {
            throw new ForbiddenException("messaging_blocked");
        }
    }

    /// <summary>Copies the upload to a temporary file, enforcing the size limit while reading and hashing the bytes.</summary>
    private static async Task<BufferedUpload> BufferAsync(Stream content, long maxSize, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), "chatx-upload-" + Guid.NewGuid().ToString("N"));
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = await content.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maxSize)
                {
                    throw new PayloadTooLargeException("attachment_too_large");
                }

                hash.AppendData(chunk, 0, read);
                await file.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            if (total == 0)
            {
                throw new RequestValidationException("file", "attachment_empty");
            }

            await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            file.Position = 0;
            return new BufferedUpload(file, total, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch
        {
            await file.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed record BufferedUpload(FileStream Stream, long Length, string Sha256) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Stream.DisposeAsync();
    }
}
