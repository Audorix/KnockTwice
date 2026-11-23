namespace Audorix.ChatX.Domain.Conversations;

/// <summary>
/// A message pinned in its conversation (Phase 18, ADR-032). Owner decision: in direct
/// conversations either person pins, in groups and channels owners and administrators; at most
/// <see cref="MaxPerConversation"/> per conversation. Pins go with their message.
/// </summary>
public sealed class MessagePin
{
    public const int MaxPerConversation = 50;

    private MessagePin(Guid id, Guid conversationId, Guid entryId, Guid pinnedByUserId, long pinnedTicks)
    {
        Id = id;
        ConversationId = conversationId;
        EntryId = entryId;
        PinnedByUserId = pinnedByUserId;
        PinnedTicks = pinnedTicks;
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid EntryId { get; private set; }

    public Guid PinnedByUserId { get; private set; }

    /// <summary>When it was pinned (UTC ticks, for ordering on every database).</summary>
    public long PinnedTicks { get; private set; }

    public static MessagePin Create(Guid id, ConversationEvent message, Guid userId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!message.IsMessage || message.IsDeleted || !message.InMainTimeline)
        {
            throw new DomainRuleException("message_not_found", "Only messages shown in the conversation can be pinned.");
        }

        return new(MessageReaction.RequireId(id), message.ConversationId, message.Id, MessageReaction.RequireId(userId), nowUtc.UtcTicks);
    }
}

/// <summary>
/// A person's bookmark of a message (Phase 18). Owner decision: shown only while the person can
/// still read the message; no copy is kept.
/// </summary>
public sealed class SavedMessage
{
    public const int MaxPerUser = 1000;

    private SavedMessage(Guid id, Guid userId, Guid conversationId, Guid entryId, long savedTicks)
    {
        Id = id;
        UserId = userId;
        ConversationId = conversationId;
        EntryId = entryId;
        SavedTicks = savedTicks;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid EntryId { get; private set; }

    public long SavedTicks { get; private set; }

    public static SavedMessage Create(Guid id, ConversationEvent message, Guid userId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!message.IsMessage || message.IsDeleted)
        {
            throw new DomainRuleException("message_not_found", "Only messages can be saved.");
        }

        return new(MessageReaction.RequireId(id), MessageReaction.RequireId(userId), message.ConversationId, message.Id, nowUtc.UtcTicks);
    }
}
