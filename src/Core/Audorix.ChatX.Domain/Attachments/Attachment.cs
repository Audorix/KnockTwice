using System.Text;
using Audorix.ChatX.Domain.Organizations;

namespace Audorix.ChatX.Domain.Attachments;

/// <summary>Upload lifecycle (Architecture §17.5). Only <see cref="Ready"/> files can be linked or downloaded.</summary>
public enum AttachmentStatus
{
    /// <summary>The record exists and the bytes are being written to storage.</summary>
    PendingUpload = 1,

    /// <summary>Stored and validated, waiting for a malware scan (or for the scanner to come back).</summary>
    Quarantined = 2,

    Ready = 3,

    /// <summary>Validation or the scan failed; the stored bytes are removed.</summary>
    Rejected = 4,

    /// <summary>Removed by its owner, with its message, or as an abandoned upload.</summary>
    Deleted = 5,
}

/// <summary>
/// A file uploaded into a conversation (ADR-007: metadata in the database, content in the
/// configured storage provider under a generated key). The upload binds owner and conversation
/// before any bytes are accepted; a message may only link a ready attachment of its own sender and
/// conversation. Storage and database do not share a transaction, so content removal is tracked
/// separately (<see cref="ContentRemoved"/>) and retried by the cleanup job.
/// </summary>
public sealed class Attachment
{
    public const int FileNameMaxLength = 200;
    public const int StorageKeyMaxLength = 200;
    public const int ContentTypeMaxLength = 100;

    private Attachment(Guid id, Guid conversationId, Guid ownerUserId, string storageKey, string fileName, DateTimeOffset createdAtUtc)
    {
        Id = id;
        ConversationId = conversationId;
        OwnerUserId = ownerUserId;
        StorageKey = storageKey;
        FileName = fileName;
        CreatedAtUtc = createdAtUtc;
        ContentType = "application/octet-stream";
        Sha256 = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    /// <summary>Generated, never derived from the user's file name.</summary>
    public string StorageKey { get; private set; }

    /// <summary>The sanitized original file name, shown to people and used for downloads.</summary>
    public string FileName { get; private set; }

    /// <summary>The type detected from the content signature, not the one the client claimed.</summary>
    public string ContentType { get; private set; }

    public long Size { get; private set; }

    /// <summary>Lower-case hex SHA-256 of the stored bytes.</summary>
    public string Sha256 { get; private set; }

    public AttachmentStatus Status { get; private set; } = AttachmentStatus.PendingUpload;

    /// <summary>Why the file was rejected (an error code), if it was.</summary>
    public string? RejectionCode { get; private set; }

    /// <summary>The message (history entry) the attachment belongs to once sent.</summary>
    public Guid? MessageEntryId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ReadyAtUtc { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <summary>True once the stored bytes are gone (or were never written).</summary>
    public bool ContentRemoved { get; private set; }

    public bool IsReady => Status == AttachmentStatus.Ready;

    public bool IsLinked => MessageEntryId is not null;

    public static Attachment Begin(Guid id, Guid conversationId, Guid ownerUserId, string storageKey, string fileName, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > StorageKeyMaxLength)
        {
            throw new DomainRuleException("value_invalid", "A storage key is required.");
        }

        return new(Require.Id(id), Require.Id(conversationId), Require.Id(ownerUserId), storageKey, NormalizeFileName(fileName), Require.Utc(nowUtc));
    }

    /// <summary>The bytes are stored and passed validation; they wait for the scan.</summary>
    public void Stored(long size, string contentType, string sha256)
    {
        RequireStatus(AttachmentStatus.PendingUpload);
        if (size <= 0)
        {
            throw new DomainRuleException("attachment_empty", "The file is empty.");
        }

        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > ContentTypeMaxLength)
        {
            throw new DomainRuleException("value_invalid", "A content type is required.");
        }

        Size = size;
        ContentType = contentType;
        Sha256 = sha256 ?? string.Empty;
        Status = AttachmentStatus.Quarantined;
    }

    public void MarkReady(DateTimeOffset nowUtc)
    {
        RequireStatus(AttachmentStatus.Quarantined);
        Status = AttachmentStatus.Ready;
        ReadyAtUtc = Require.Utc(nowUtc);
    }

    /// <summary>Validation or scanning failed. The caller removes the bytes and then calls <see cref="ContentWasRemoved"/>.</summary>
    public void Reject(string code, DateTimeOffset nowUtc)
    {
        if (Status is AttachmentStatus.Ready or AttachmentStatus.Deleted or AttachmentStatus.Rejected)
        {
            throw new DomainRuleException("attachment_not_pending", "Only files that are still being processed can be rejected.");
        }

        Status = AttachmentStatus.Rejected;
        RejectionCode = string.IsNullOrWhiteSpace(code) ? "attachment_rejected" : code;
        DeletedAtUtc = Require.Utc(nowUtc);
    }

    /// <summary>Links a ready, unlinked attachment to a message of the same conversation and sender.</summary>
    public void Link(Guid conversationId, Guid authorUserId, Guid messageEntryId)
    {
        if (!IsReady || IsLinked || conversationId != ConversationId || authorUserId != OwnerUserId)
        {
            throw new DomainRuleException("attachment_not_available", "The attachment cannot be used in this message.");
        }

        MessageEntryId = Require.Id(messageEntryId);
    }

    /// <summary>Deletes the attachment (idempotent). The content is removed afterwards.</summary>
    public void Delete(DateTimeOffset nowUtc)
    {
        if (Status == AttachmentStatus.Deleted)
        {
            return;
        }

        Status = AttachmentStatus.Deleted;
        DeletedAtUtc ??= Require.Utc(nowUtc);
    }

    public void ContentWasRemoved() => ContentRemoved = true;

    /// <summary>
    /// Keeps only the last path segment, removes control and reserved characters and limits the
    /// length while keeping the extension. Never used to build storage paths.
    /// </summary>
    public static string NormalizeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormC))
        {
            if (char.IsControl(c) || c is '"' or '<' or '>' or '|' or ':' or '*' or '?' || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format)
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(c);
            }
        }

        name = builder.ToString().Trim().Trim('.').Trim();
        if (name.Length == 0)
        {
            throw new DomainRuleException("attachment_name_invalid", "The file needs a name.");
        }

        if (name.Length > FileNameMaxLength)
        {
            var dot = name.LastIndexOf('.');
            var extension = dot > 0 && name.Length - dot <= 16 ? name[dot..] : string.Empty;
            name = name[..(FileNameMaxLength - extension.Length)] + extension;
        }

        return name;
    }

    /// <summary>Lower-case extension without the dot, or an empty string.</summary>
    public static string ExtensionOf(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 && dot < fileName.Length - 1 ? fileName[(dot + 1)..].ToLowerInvariant() : string.Empty;
    }

    private void RequireStatus(AttachmentStatus status)
    {
        if (Status != status)
        {
            throw new DomainRuleException("attachment_not_pending", "The attachment is not in the expected state.");
        }
    }
}
