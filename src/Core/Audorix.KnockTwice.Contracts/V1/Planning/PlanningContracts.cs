namespace Audorix.KnockTwice.Contracts.V1.Planning;

/// <param name="Body">The draft text as typed. Empty text without a reply target removes the draft.</param>
/// <param name="ThreadRootId">Set for a draft in a thread.</param>
public sealed record SaveDraftRequest(string? Body, Guid? ReplyToId, Guid? ThreadRootId);

public sealed record DraftResponse(Guid ConversationId, Guid? ThreadRootId, string Body, Guid? ReplyToId, DateTimeOffset UpdatedAtUtc);

/// <param name="SendAtUtc">When to send: in the future, at most 30 days ahead.</param>
/// <param name="AttachmentIds">Your unsent uploads in this conversation; they are kept until the message goes out.</param>
public sealed record ScheduleMessageRequest(
    string? Body, DateTimeOffset SendAtUtc, Guid? ReplyToId = null, Guid? ThreadRootId = null, bool AlsoInConversation = false,
    IReadOnlyList<Guid>? AttachmentIds = null, IReadOnlyList<Guid>? MentionUserIds = null);

public sealed record UpdateScheduledRequest(string? Body, DateTimeOffset SendAtUtc);

/// <param name="Status"><c>pending</c>, or <c>failed</c> with <paramref name="FailureCode"/> (the error that stopped it).</param>
public sealed record ScheduledMessageResponse(
    Guid Id, Guid ConversationId, string? ConversationType, string? ConversationTitle, string Body, DateTimeOffset SendAtUtc, string Status, string? FailureCode,
    int FileCount, Guid? ThreadRootId, Guid? ReplyToId);

/// <param name="RemindAtUtc">When to remind you: in the future, at most a year ahead.</param>
public sealed record SetReminderRequest(DateTimeOffset RemindAtUtc);

/// <param name="Status"><c>pending</c>, or <c>due</c> once its time came (until you mark it done).</param>
public sealed record ReminderResponse(
    Guid Id, Guid ConversationId, string ConversationType, string? ConversationTitle, Conversations.ConversationEntryResponse Entry, DateTimeOffset RemindAtUtc, string Status);
