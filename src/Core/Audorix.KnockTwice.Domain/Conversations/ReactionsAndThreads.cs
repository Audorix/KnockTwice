using System.Globalization;

namespace Audorix.KnockTwice.Domain.Conversations;

/// <summary>
/// One person's emoji reaction to a message (Phase 16, owner decision: any emoji, at most
/// <see cref="MaxDistinctPerMessage"/> different ones per message). Unique per message, person and emoji.
/// </summary>
public sealed class MessageReaction
{
    public const int EmojiMaxLength = 32;
    public const int MaxDistinctPerMessage = 20;

    private MessageReaction(Guid id, Guid conversationId, Guid entryId, Guid userId, string emoji, DateTimeOffset createdAtUtc)
    {
        Id = id;
        ConversationId = conversationId;
        EntryId = entryId;
        UserId = userId;
        Emoji = emoji;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid EntryId { get; private set; }

    public Guid UserId { get; private set; }

    public string Emoji { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static MessageReaction Create(Guid id, ConversationEvent message, Guid userId, string emoji, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!message.IsMessage || message.IsDeleted)
        {
            throw new DomainRuleException("message_not_found", "The message does not exist or was deleted.");
        }

        return new(RequireId(id), message.ConversationId, message.Id, RequireId(userId), NormalizeEmoji(emoji), nowUtc.ToUniversalTime());
    }

    internal static Guid RequireId(Guid id) => id == Guid.Empty ? throw new ArgumentException("An id is required.", nameof(id)) : id;

    /// <summary>
    /// Exactly one emoji: a single text element (so skin tones, flags and joined sequences count as
    /// one) that contains a symbol and no letters, digits, spaces or control characters.
    /// </summary>
    public static string NormalizeEmoji(string? emoji)
    {
        var text = emoji?.Trim() ?? string.Empty;
        var valid = text.Length is > 0 and <= EmojiMaxLength
            && new StringInfo(text).LengthInTextElements == 1
            && !text.Any(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsControl(c))
            && text.Any(c => char.IsSurrogate(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.OtherSymbol or UnicodeCategory.MathSymbol);
        return valid ? text : throw new DomainRuleException("reaction_invalid", "A reaction is a single emoji.");
    }
}

/// <summary>How a person was mentioned (Phase 17).</summary>
public enum MentionKind
{
    /// <summary>By name (<c>@Ana</c>).</summary>
    User = 1,

    /// <summary>Through <c>@everyone</c>: every participant.</summary>
    Everyone = 2,

    /// <summary>Through <c>@here</c>: participants online when the message was sent.</summary>
    Here = 3,
}

/// <summary>
/// One person mentioned by one message (Phase 17, ADR-031). <c>@everyone</c> and <c>@here</c> are
/// expanded to a row per recipient when the message is sent, so a person's mentions are a simple
/// lookup. Mentions are fixed at sending; they go with the message.
/// </summary>
public sealed class MessageMention
{
    private MessageMention(Guid id, Guid conversationId, Guid entryId, Guid userId, MentionKind kind, long sentTicks)
    {
        Id = id;
        ConversationId = conversationId;
        EntryId = entryId;
        UserId = userId;
        Kind = kind;
        SentTicks = sentTicks;
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid EntryId { get; private set; }

    /// <summary>The person mentioned.</summary>
    public Guid UserId { get; private set; }

    public MentionKind Kind { get; private set; }

    /// <summary>When the message was sent (UTC ticks, for ordering on every database).</summary>
    public long SentTicks { get; private set; }

    public static MessageMention Create(Guid id, ConversationEvent message, Guid userId, MentionKind kind)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new(MessageReaction.RequireId(id), message.ConversationId, message.Id, MessageReaction.RequireId(userId),
            Enum.IsDefined(kind) ? kind : throw new ArgumentOutOfRangeException(nameof(kind)), message.CreatedAtUtc.UtcTicks);
    }
}

/// <summary>
/// A person's state in one thread (Phase 16): whether they follow it (authors of the first message
/// and of replies follow automatically) and how far they have read it. Thread unread counts and
/// thread notifications use this, separately from the conversation's read position.
/// </summary>
public sealed class ThreadFollow
{
    private ThreadFollow(Guid id, Guid conversationId, Guid rootId, Guid userId, long lastReadSequence, DateTimeOffset createdAtUtc)
    {
        Id = id;
        ConversationId = conversationId;
        RootId = rootId;
        UserId = userId;
        LastReadSequence = lastReadSequence;
        Following = true;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid RootId { get; private set; }

    public Guid UserId { get; private set; }

    public bool Following { get; private set; }

    /// <summary>The highest thread position the person has read. Moves forward only.</summary>
    public long LastReadSequence { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static ThreadFollow Start(Guid id, ConversationEvent root, Guid userId, long readUpTo, bool following, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new(MessageReaction.RequireId(id), root.ConversationId, root.Id, MessageReaction.RequireId(userId), Math.Max(readUpTo, root.Sequence), nowUtc.ToUniversalTime())
        {
            Following = following,
        };
    }

    public void SetFollowing(bool following) => Following = following;

    public bool MarkRead(long sequence)
    {
        if (sequence <= LastReadSequence)
        {
            return false;
        }

        LastReadSequence = sequence;
        return true;
    }
}
