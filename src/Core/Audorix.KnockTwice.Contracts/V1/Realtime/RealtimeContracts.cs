namespace Audorix.KnockTwice.Contracts.V1.Realtime;

/// <summary>
/// SignalR contract of <c>/hubs/chat</c> (JSON protocol, camelCase). Server events carry a
/// <c>version</c>; a breaking change adds a new event name or version instead of altering one.
/// Hub method failures are reported as a <c>HubException</c> whose message is a stable error code
/// (<c>conversation_not_found</c>, <c>unauthorized</c>, <c>validation_failed</c>).
/// </summary>
public static class RealtimeContract
{
    public const string HubPath = "/hubs/chat";
    public const int Version = 1;

    /// <summary>Client-to-server hub methods.</summary>
    public static class Methods
    {
        public const string JoinConversation = "JoinConversation";
        public const string LeaveConversation = "LeaveConversation";
        public const string SendTypingStatus = "SendTypingStatus";
        public const string GetPresence = "GetPresence";
    }

    /// <summary>Server-to-client events implemented so far. Message, receipt and notification events arrive with their phases.</summary>
    public static class Events
    {
        public const string UserPresenceChanged = "UserPresenceChanged";
        public const string TypingStatusChanged = "TypingStatusChanged";
        public const string ConversationUpdated = "ConversationUpdated";
        public const string MessageCreated = "MessageCreated";
        public const string MessageUpdated = "MessageUpdated";
        public const string MessageDeleted = "MessageDeleted";
        public const string MessageDelivered = "MessageDelivered";
        public const string MessageRead = "MessageRead";
        public const string PrivateChatChanged = "PrivateChatChanged";
        public const string PrivateMessageCreated = "PrivateMessageCreated";
    }
}

/// <summary>
/// A private chat was offered to you (<c>invited</c>), accepted (<c>active</c>) or ended (<c>ended</c>,
/// with a <paramref name="Reason"/>: <c>ended</c>, <c>declined</c>, <c>expired</c>, <c>session_ended</c>,
/// <c>unavailable</c> or <c>shutdown</c>). After <c>ended</c> forget everything you showed.
/// </summary>
public sealed record PrivateChatChangedEvent(int Version, Guid ChatId, string Status, string? Reason);

/// <summary>A message in a private chat. It is not stored on the server; keep it in memory only.</summary>
public sealed record PrivateMessageEvent(int Version, Guid ChatId, PrivateChats.PrivateMessageResponse Message);

/// <summary>
/// A message was created, edited or deleted (published from the transactional outbox, at least
/// once). Deduplicate by <paramref name="EventId"/> and keep the highest <c>entry.version</c>.
/// </summary>
public sealed record MessageEvent(int Version, Guid EventId, Guid ConversationId, Conversations.ConversationEntryResponse Entry);

/// <summary>A participant's delivery or read watermark moved forward (ADR-023).</summary>
public sealed record ReceiptEvent(int Version, Guid ConversationId, Guid UserId, long Sequence);

/// <summary>Phase 20: your drafts, scheduled messages or reminders changed (<paramref name="Kind"/>: <c>drafts</c>, <c>scheduled</c>, <c>reminders</c>).</summary>
public sealed record PlanningChangedEvent(int Version, string Kind);

/// <summary>Phase 20: a reminder came due. <paramref name="Excerpt"/> is plain text.</summary>
public sealed record ReminderDueEvent(int Version, Guid ReminderId, Guid ConversationId, Guid MessageId, Guid? ThreadRootId, string? AuthorName, string? Excerpt);

/// <summary>Phase 16: the reactions of a message changed; replaces the message's reactions.</summary>
public sealed record ReactionChangedEvent(int Version, Guid ConversationId, Guid MessageId, IReadOnlyList<Conversations.ReactionResponse> Reactions);

/// <summary>
/// A conversation the user takes part in changed; reload the list or the open conversation.
/// <paramref name="Change"/>: <c>created</c>, <c>updated</c>, <c>members</c>, <c>read</c>, or <c>removed</c>
/// (the user no longer has access).
/// </summary>
public sealed record ConversationUpdatedEvent(int Version, Guid ConversationId, string Change, long LastSequence);

/// <param name="Status"><c>online</c> or <c>offline</c>.</param>
public sealed record UserPresenceChangedEvent(int Version, Guid UserId, string Status, DateTimeOffset AtUtc);

/// <param name="ExpiresAtUtc">When a "typing" state ends unless renewed; null when <paramref name="IsTyping"/> is false.</param>
public sealed record TypingStatusChangedEvent(int Version, Guid ConversationId, Guid UserId, bool IsTyping, DateTimeOffset? ExpiresAtUtc);

/// <param name="Status"><c>online</c> or <c>offline</c>.</param>
public sealed record UserPresenceResponse(Guid UserId, string Status);
