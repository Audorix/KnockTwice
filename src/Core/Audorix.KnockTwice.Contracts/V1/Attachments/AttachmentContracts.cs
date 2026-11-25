namespace Audorix.KnockTwice.Contracts.V1.Attachments;

/// <summary>An attachment shown with its message. Download it from <c>/api/v1/attachments/{id}/content</c>.</summary>
/// <param name="ContentType">Detected from the file content (signature), not taken from the uploader.</param>
/// <param name="Kind"><c>image</c> (may be shown inline) or <c>file</c>.</param>
/// <param name="Size">Bytes.</param>
public sealed record MessageAttachmentResponse(Guid Id, string FileName, string ContentType, string Kind, long Size);

/// <summary>An uploaded attachment.</summary>
/// <param name="Status"><c>pending_upload</c>, <c>quarantined</c> (waiting for a malware scan), <c>ready</c>, <c>rejected</c> or <c>deleted</c>. Only ready attachments can be sent.</param>
/// <param name="MessageId">The message it was sent with; null while unsent.</param>
public sealed record AttachmentResponse(
    Guid Id, Guid ConversationId, string FileName, string ContentType, string Kind, long Size, string Status, Guid? MessageId, DateTimeOffset CreatedAtUtc);

/// <summary>The installation's attachment limits, for clients to check before uploading.</summary>
/// <param name="AllowedExtensions">Lower-case extensions without dot.</param>
/// <param name="UserQuota">Bytes per person; 0 = unlimited.</param>
public sealed record AttachmentLimitsResponse(long MaxFileSize, int MaxPerMessage, long UserQuota, IReadOnlyList<string> AllowedExtensions, bool ScanningEnabled);
