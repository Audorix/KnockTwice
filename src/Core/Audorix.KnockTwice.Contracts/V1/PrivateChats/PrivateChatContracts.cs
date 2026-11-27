using Audorix.KnockTwice.Contracts.V1.Users;

namespace Audorix.KnockTwice.Contracts.V1.PrivateChats;

/// <summary>
/// A private chat: one-to-one, held only in server memory, never saved. It lives as long as the
/// initiator's sign-in session and is wiped (messages and files) when it ends.
/// </summary>
/// <param name="Status"><c>invited</c> (waiting for the other person) or <c>active</c>.</param>
/// <param name="IsInitiator">True for the person who started it (and whose session it lives in).</param>
/// <param name="UsedBytes">Memory used by this chat's files.</param>
public sealed record PrivateChatResponse(
    Guid Id,
    string Status,
    bool IsInitiator,
    UserSummaryResponse? Counterpart,
    UserSummaryResponse? Initiator,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AcceptedAtUtc,
    DateTimeOffset InvitationExpiresAtUtc,
    long UsedBytes,
    long MaxBytes,
    long MaxFileSize,
    Guid? OrganizationId = null);

/// <param name="Messages">Everything said so far, oldest first; it exists only in memory.</param>
public sealed record PrivateChatDetailResponse(PrivateChatResponse Chat, IReadOnlyList<PrivateMessageResponse> Messages);

/// <param name="Kind"><c>image</c> or <c>file</c>.</param>
public sealed record PrivateAttachmentResponse(Guid Id, string FileName, string ContentType, string Kind, long Size);

public sealed record PrivateMessageResponse(
    Guid Id, long Sequence, UserSummaryResponse? Author, Guid AuthorId, string ClientMessageId, string Body,
    IReadOnlyList<PrivateAttachmentResponse> Attachments, DateTimeOffset CreatedAtUtc);

public sealed record StartPrivateChatRequest(Guid UserId, Guid? OrganizationId = null);

/// <param name="ClientMessageId">Sender-generated id, 1-64 characters; a retry returns the original.</param>
/// <param name="AttachmentIds">Files uploaded into this private chat by the sender.</param>
public sealed record SendPrivateMessageRequest(string? ClientMessageId, string? Body, IReadOnlyList<Guid>? AttachmentIds);
