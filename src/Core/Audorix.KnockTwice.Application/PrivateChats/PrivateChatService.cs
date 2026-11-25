using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Contacts;
using Audorix.KnockTwice.Application.Messaging;
using Audorix.KnockTwice.Application.Organizations;
using Audorix.KnockTwice.Application.Realtime;
using Audorix.KnockTwice.Application.Sessions;
using Audorix.KnockTwice.Application.Users;
using Audorix.KnockTwice.Domain.Attachments;
using Audorix.KnockTwice.Domain.Contacts;
using Audorix.KnockTwice.Domain.Conversations;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Application.PrivateChats;

/// <summary>
/// Private (ephemeral) chats, requested by the owner for Phase 7 (decisions of 2026-11-22):
/// <list type="bullet">
/// <item>One-to-one, between people who could have a direct conversation (contacts or members of a shared organization, never across a block).</item>
/// <item>The other person must accept an invitation that explains the rules before anything is sent.</item>
/// <item>The chat lives as long as the initiator's sign-in session: it ends when they end it, sign out, or the session is revoked or expires. The invitee may close and reopen it meanwhile.</item>
/// <item>Messages and attachments exist only in this server's memory, are never written to the database, storage or logs, and are wiped when the chat ends. A restart ends every private chat.</item>
/// </list>
/// The initiator reaches the chat only from the session that started it; the invitee from any of their sessions.
/// </summary>
public sealed class PrivateChatService(
    PrivateChatRegistry registry,
    RealtimeState realtime,
    IRealtimePublisher publisher,
    SessionService sessions,
    IContactStore contacts,
    IOrganizationStore organizations,
    IUserDirectory directory,
    IMalwareScanner scanner,
    TimeProvider time,
    IOptionsSnapshot<PrivateChatSettings> settings,
    IOptionsSnapshot<MessagingSettings> messaging,
    IOptionsSnapshot<AttachmentSettings> attachmentSettings)
{
    private const string NotFound = "private_chat_not_found";

    // ---- Lifecycle ---------------------------------------------------------------------------

    /// <summary>Invites another person to a private chat bound to the caller's current session.</summary>
    public async Task<(PrivateChatView View, bool Created)> StartAsync(Guid userId, Guid sessionId, Guid otherUserId, CancellationToken cancellationToken)
    {
        RequireEnabled();
        if (otherUserId == userId)
        {
            throw new RequestValidationException("userId", "cannot_target_self");
        }

        if (!await MayTalkAsync(userId, otherUserId, cancellationToken).ConfigureAwait(false))
        {
            throw new ResourceNotFoundException("user_not_found");
        }

        var options = settings.Value;
        var running = registry.All().Where(c => c.Status != PrivateChatStatus.Ended).ToList();
        if (running.FirstOrDefault(c => c.Involves(userId) && c.Involves(otherUserId)) is { } existing)
        {
            // One private chat per pair at a time; the initiator's own session gets it back.
            return existing.InitiatorUserId == userId && existing.InitiatorSessionId == sessionId
                ? (await ViewAsync(existing, userId, cancellationToken).ConfigureAwait(false), false)
                : throw new ConflictException("private_chat_exists");
        }

        if (running.Count(c => c.Involves(userId)) >= options.MaxActivePerUser || running.Count(c => c.Involves(otherUserId)) >= options.MaxActivePerUser)
        {
            throw new ConflictException("private_chat_limit");
        }

        var now = time.GetUtcNow();
        var chat = new PrivateChat(Guid.CreateVersion7(now), userId, sessionId, otherUserId, now, now + options.InvitationTimeout);
        registry.Add(chat);
        await PublishAsync(chat, new PrivateChatChanged(chat.Id, "invited", null), cancellationToken).ConfigureAwait(false);
        return (await ViewAsync(chat, userId, cancellationToken).ConfigureAwait(false), true);
    }

    /// <summary>The invitee accepts; from now on both can send.</summary>
    public async Task<PrivateChatView> AcceptAsync(Guid userId, Guid sessionId, Guid chatId, CancellationToken cancellationToken)
    {
        var chat = Require(chatId, userId, sessionId);
        if (chat.InviteeUserId != userId)
        {
            throw new ForbiddenException();
        }

        if (!await MayTalkAsync(chat.InitiatorUserId, chat.InviteeUserId, cancellationToken).ConfigureAwait(false))
        {
            await EndAsync(chat, PrivateChatEndReasons.Unavailable, cancellationToken).ConfigureAwait(false);
            throw new ResourceNotFoundException(NotFound);
        }

        lock (chat.Gate)
        {
            if (chat.Status == PrivateChatStatus.Invited)
            {
                chat.Status = PrivateChatStatus.Active;
                chat.AcceptedAtUtc = time.GetUtcNow();
            }
            else if (chat.Status != PrivateChatStatus.Active)
            {
                throw new ResourceNotFoundException(NotFound);
            }
        }

        await PublishAsync(chat, new PrivateChatChanged(chat.Id, "active", null), cancellationToken).ConfigureAwait(false);
        return await ViewAsync(chat, userId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The invitee declines, or the initiator ends the chat (or cancels the invitation). Either way it is gone.</summary>
    public async Task EndAsync(Guid userId, Guid sessionId, Guid chatId, CancellationToken cancellationToken)
    {
        var chat = Require(chatId, userId, sessionId);
        if (chat.InviteeUserId == userId)
        {
            // The invitee may only decline an invitation; an active chat belongs to the initiator's session.
            if (chat.Status != PrivateChatStatus.Invited)
            {
                throw new ForbiddenException("private_chat_initiator_only");
            }

            await EndAsync(chat, PrivateChatEndReasons.Declined, cancellationToken).ConfigureAwait(false);
            return;
        }

        await EndAsync(chat, PrivateChatEndReasons.Ended, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ends the chats started from a session (sign-out or revocation). Returns how many ended.</summary>
    public async Task<int> EndForSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var ending = registry.All().Where(c => c.InitiatorSessionId == sessionId).ToList();
        foreach (var chat in ending)
        {
            await EndAsync(chat, PrivateChatEndReasons.SessionEnded, cancellationToken).ConfigureAwait(false);
        }

        return ending.Count;
    }

    /// <summary>Ends the chats between two people, for example after one blocked the other.</summary>
    public async Task EndBetweenAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken)
    {
        foreach (var chat in registry.All().Where(c => c.Involves(userId) && c.Involves(otherUserId)))
        {
            await EndAsync(chat, PrivateChatEndReasons.Unavailable, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Ends every chat the person takes part in, either side (account deletion, Phase 13).</summary>
    public async Task EndForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (var chat in registry.All().Where(c => c.Involves(userId)).ToList())
        {
            await EndAsync(chat, PrivateChatEndReasons.Unavailable, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Expires invitations and ends chats whose initiator session is no longer valid.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var ended = 0;
        var now = time.GetUtcNow();
        foreach (var chat in registry.All())
        {
            string? reason = null;
            if (chat.Status == PrivateChatStatus.Invited && now >= chat.InvitationExpiresAtUtc)
            {
                reason = PrivateChatEndReasons.Expired;
            }
            else if (!await sessions.ValidateAsync(chat.InitiatorSessionId, chat.InitiatorUserId, cancellationToken).ConfigureAwait(false))
            {
                reason = PrivateChatEndReasons.SessionEnded;
            }

            if (reason is not null)
            {
                await EndAsync(chat, reason, cancellationToken).ConfigureAwait(false);
                ended++;
            }
        }

        return ended;
    }

    /// <summary>Ends everything (host shutdown): nothing survives the process.</summary>
    public async Task EndAllAsync(CancellationToken cancellationToken)
    {
        foreach (var chat in registry.All())
        {
            await EndAsync(chat, PrivateChatEndReasons.Shutdown, cancellationToken).ConfigureAwait(false);
        }
    }

    // ---- Reading -----------------------------------------------------------------------------

    /// <summary>The caller's running private chats: those started in this session, and invitations or chats with them as invitee.</summary>
    public async Task<IReadOnlyList<PrivateChatView>> ListAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var views = new List<PrivateChatView>();
        foreach (var chat in registry.All().Where(c => c.Status != PrivateChatStatus.Ended && CanSee(c, userId, sessionId)).OrderByDescending(c => c.CreatedAtUtc))
        {
            views.Add(await ViewAsync(chat, userId, cancellationToken).ConfigureAwait(false));
        }

        return views;
    }

    public async Task<PrivateChatDetailView> GetAsync(Guid userId, Guid sessionId, Guid chatId, CancellationToken cancellationToken)
    {
        var chat = Require(chatId, userId, sessionId);
        List<PrivateMessage> messages;
        lock (chat.Gate)
        {
            messages = [.. chat.Messages];
        }

        var people = await PeopleAsync(chat, cancellationToken).ConfigureAwait(false);
        return new PrivateChatDetailView(
            await ViewAsync(chat, userId, cancellationToken).ConfigureAwait(false),
            messages.Select(m => ToView(m, people)).ToList());
    }

    // ---- Messages and files ------------------------------------------------------------------

    /// <summary>Sends a message into an active chat (idempotent by client message id while the chat lasts).</summary>
    public async Task<(PrivateMessageView Message, bool Created)> SendAsync(
        Guid userId, Guid sessionId, Guid chatId, string? clientMessageId, string? body, IReadOnlyList<Guid>? attachmentIds, CancellationToken cancellationToken)
    {
        var chat = RequireActive(chatId, userId, sessionId);
        if (!await MayTalkAsync(chat.InitiatorUserId, chat.InviteeUserId, cancellationToken).ConfigureAwait(false))
        {
            await EndAsync(chat, PrivateChatEndReasons.Unavailable, cancellationToken).ConfigureAwait(false);
            throw new ResourceNotFoundException(NotFound);
        }

        var key = clientMessageId?.Trim() ?? string.Empty;
        if (key.Length is 0 or > ConversationEvent.ClientMessageIdMaxLength || key.Any(char.IsControl))
        {
            throw new RequestValidationException("clientMessageId", "client_message_id_invalid");
        }

        var files = attachmentIds ?? [];
        if (files.Count > attachmentSettings.Value.MaxPerMessage)
        {
            throw new RequestValidationException("attachmentIds", "too_many_attachments");
        }

        if (files.Distinct().Count() != files.Count)
        {
            throw new RequestValidationException("attachmentIds", "attachment_duplicate");
        }

        var text = ConversationEvent.NormalizeBody(body, messaging.Value.MaxLength, allowEmpty: files.Count > 0);
        PrivateMessage message;
        var created = false;
        lock (chat.Gate)
        {
            if (chat.Status != PrivateChatStatus.Active)
            {
                throw new ResourceNotFoundException(NotFound);
            }

            if (chat.Messages.FirstOrDefault(m => m.AuthorUserId == userId && m.ClientMessageId == key) is { } original)
            {
                message = original.Body == text && original.Files.Select(f => f.Id).ToHashSet().SetEquals(files)
                    ? original
                    : throw new ConflictException("idempotency_conflict");
            }
            else
            {
                var linked = new List<PrivateFile>();
                foreach (var id in files)
                {
                    if (!chat.Files.TryGetValue(id, out var file) || file.OwnerUserId != userId || file.Linked)
                    {
                        throw new RequestValidationException("attachmentIds", "attachment_not_available");
                    }

                    linked.Add(file);
                }

                foreach (var file in linked)
                {
                    file.Linked = true;
                }

                message = new PrivateMessage(Guid.CreateVersion7(time.GetUtcNow()), chat.NextSequence++, userId, key, text, linked, time.GetUtcNow());
                chat.Messages.Add(message);
                created = true;

                // Bounded memory: the oldest messages (and their files) are forgotten.
                while (chat.Messages.Count > settings.Value.MaxMessages)
                {
                    var oldest = chat.Messages[0];
                    chat.Messages.RemoveAt(0);
                    foreach (var file in oldest.Files)
                    {
                        Array.Clear(file.Data);
                        chat.Files.Remove(file.Id);
                    }
                }
            }
        }

        var view = ToView(message, await PeopleAsync(chat, cancellationToken).ConfigureAwait(false));
        if (created)
        {
            await PublishAsync(chat, new PrivateMessageCreated(chat.Id, view), cancellationToken).ConfigureAwait(false);
        }

        return (view, created);
    }

    /// <summary>
    /// Keeps a file in memory for a later message. Same type allow-list and signature checks as
    /// regular attachments, with the private chat's own size limits. When scanning is configured
    /// an unavailable scanner refuses the file (it cannot wait in quarantine without storage).
    /// </summary>
    public async Task<PrivateFileView> UploadAsync(
        Guid userId, Guid sessionId, Guid chatId, string? fileName, string? declaredContentType, long? declaredLength, Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!attachmentSettings.Value.Enabled)
        {
            throw new FeatureDisabledException("attachments_disabled");
        }

        var chat = RequireActive(chatId, userId, sessionId);
        var options = settings.Value;
        var name = Attachment.NormalizeFileName(fileName);
        var type = FileTypes.RequireAllowed(name, attachmentSettings.Value.EffectiveExtensions);
        FileTypes.RequireDeclaredMatches(type, declaredContentType);
        if (declaredLength > options.MaxFileSize)
        {
            throw new PayloadTooLargeException("attachment_too_large");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > options.MaxFileSize)
            {
                CryptographicClear(buffer);
                throw new PayloadTooLargeException("attachment_too_large");
            }

            buffer.Write(chunk, 0, read);
        }

        Array.Clear(chunk);
        if (buffer.Length == 0)
        {
            throw new RequestValidationException("file", "attachment_empty");
        }

        try
        {
            FileTypes.RequireSignature(type, buffer);
            if (scanner.Enabled)
            {
                buffer.Position = 0;
                var verdict = await scanner.ScanAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (verdict == ScanVerdict.Infected)
                {
                    throw new RequestValidationException("file", "attachment_infected");
                }

                if (verdict != ScanVerdict.Clean)
                {
                    throw new DependencyUnavailableException("attachment_scan_unavailable");
                }
            }

            var file = new PrivateFile(Guid.CreateVersion7(time.GetUtcNow()), userId, name, type.ContentType, FileTypes.KindOf(type.ContentType), buffer.ToArray(),
                time.GetUtcNow());
            lock (chat.Gate)
            {
                if (chat.Status != PrivateChatStatus.Active)
                {
                    Array.Clear(file.Data);
                    throw new ResourceNotFoundException(NotFound);
                }

                if (chat.UsedBytes + file.Size > options.MaxBytesPerChat)
                {
                    Array.Clear(file.Data);
                    throw new PayloadTooLargeException("private_chat_storage_full");
                }

                chat.Files[file.Id] = file;
            }

            return ToView(file);
        }
        finally
        {
            CryptographicClear(buffer);
        }
    }

    /// <summary>A copy of a file sent in the chat (or the caller's own unsent upload).</summary>
    public PrivateFile OpenFile(Guid userId, Guid sessionId, Guid chatId, Guid fileId)
    {
        var chat = Require(chatId, userId, sessionId);
        lock (chat.Gate)
        {
            if (chat.Status != PrivateChatStatus.Active
                || !chat.Files.TryGetValue(fileId, out var file)
                || (!file.Linked && file.OwnerUserId != userId))
            {
                throw new ResourceNotFoundException("attachment_not_found");
            }

            return file with { Data = (byte[])file.Data.Clone() };
        }
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private void RequireEnabled()
    {
        if (!settings.Value.Enabled)
        {
            throw new FeatureDisabledException("private_chats_disabled");
        }
    }

    /// <summary>The initiator only from the session that started it; the invitee from any session.</summary>
    private static bool CanSee(PrivateChat chat, Guid userId, Guid sessionId) =>
        (chat.InitiatorUserId == userId && chat.InitiatorSessionId == sessionId) || chat.InviteeUserId == userId;

    private PrivateChat Require(Guid chatId, Guid userId, Guid sessionId)
    {
        var chat = registry.Find(chatId);
        return chat is not null && chat.Status != PrivateChatStatus.Ended && CanSee(chat, userId, sessionId)
            ? chat
            : throw new ResourceNotFoundException(NotFound);
    }

    private PrivateChat RequireActive(Guid chatId, Guid userId, Guid sessionId)
    {
        var chat = Require(chatId, userId, sessionId);
        return chat.Status == PrivateChatStatus.Active ? chat : throw new ConflictException("private_chat_not_active");
    }

    /// <summary>Contacts, or active members of a shared organization; never across a block.</summary>
    private async Task<bool> MayTalkAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken)
    {
        if ((await contacts.ListBlockPartnersAsync(userId, cancellationToken).ConfigureAwait(false)).Contains(otherUserId))
        {
            return false;
        }

        if (await contacts.FindContactAsync(UserPair.Of(userId, otherUserId), cancellationToken).ConfigureAwait(false) is not null)
        {
            return true;
        }

        var mine = (await organizations.ListActiveMembershipsAsync(userId, cancellationToken).ConfigureAwait(false)).Select(m => m.Organization.Id).ToHashSet();
        if (mine.Count == 0)
        {
            return false;
        }

        return (await organizations.ListActiveMembershipsAsync(otherUserId, cancellationToken).ConfigureAwait(false)).Any(m => mine.Contains(m.Organization.Id));
    }

    private async Task EndAsync(PrivateChat chat, string reason, CancellationToken cancellationToken)
    {
        lock (chat.Gate)
        {
            if (chat.Status == PrivateChatStatus.Ended)
            {
                return;
            }

            chat.Status = PrivateChatStatus.Ended;
            chat.EndReason = reason;
            chat.Wipe();
        }

        registry.Remove(chat.Id);
        try
        {
            await PublishAsync(chat, new PrivateChatChanged(chat.Id, "ended", reason), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The chat is gone either way; clients that missed the event get 404 on their next request.
        }
    }

    /// <summary>The initiator's connections of the initiating session, and every connection of the invitee.</summary>
    private Task PublishAsync(PrivateChat chat, IRealtimeEvent realtimeEvent, CancellationToken cancellationToken)
    {
        var targets = realtime.Snapshot()
            .Where(c => (c.UserId == chat.InitiatorUserId && c.SessionId == chat.InitiatorSessionId) || c.UserId == chat.InviteeUserId)
            .Select(c => c.ConnectionId)
            .ToList();
        return publisher.PublishToConnectionsAsync(targets, realtimeEvent, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<Guid, UserSummary>> PeopleAsync(PrivateChat chat, CancellationToken cancellationToken) =>
        await directory.GetSummariesAsync([chat.InitiatorUserId, chat.InviteeUserId], cancellationToken).ConfigureAwait(false);

    private async Task<PrivateChatView> ViewAsync(PrivateChat chat, Guid userId, CancellationToken cancellationToken)
    {
        var people = await PeopleAsync(chat, cancellationToken).ConfigureAwait(false);
        long used;
        lock (chat.Gate)
        {
            used = chat.UsedBytes;
        }

        return new PrivateChatView(chat, chat.InitiatorUserId == userId, people.GetValueOrDefault(chat.CounterpartOf(userId)),
            people.GetValueOrDefault(chat.InitiatorUserId), used, settings.Value.MaxBytesPerChat, settings.Value.MaxFileSize);
    }

    private static PrivateMessageView ToView(PrivateMessage message, IReadOnlyDictionary<Guid, UserSummary> people) => new(
        message.Id, message.Sequence, people.GetValueOrDefault(message.AuthorUserId), message.AuthorUserId, message.ClientMessageId, message.Body,
        message.Files.Select(ToView).ToList(), message.CreatedAtUtc);

    private static PrivateFileView ToView(PrivateFile file) => new(file.Id, file.FileName, file.ContentType, file.Kind, file.Size);

    private static void CryptographicClear(MemoryStream buffer)
    {
        if (buffer.TryGetBuffer(out var segment))
        {
            Array.Clear(segment.Array!, segment.Offset, segment.Count);
        }
    }
}
