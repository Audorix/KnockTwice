namespace Audorix.ChatX.Domain.Conversations;

/// <summary>
/// An unsent message a person is writing (Phase 20, ADR-034): its text and what it replies to, per
/// conversation and thread, kept on the server so every device shows it. Owner decision: no files;
/// removed when the message is sent and after <see cref="MaxAgeDays"/> days without changes.
/// </summary>
public sealed class MessageDraft
{
    public const int MaxAgeDays = 30;

    private MessageDraft(Guid id, Guid userId, Guid conversationId, Guid threadKey)
    {
        Id = id;
        UserId = userId;
        ConversationId = conversationId;
        ThreadKey = threadKey;
        Body = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid ConversationId { get; private set; }

    /// <summary>The thread's first message, or <see cref="Guid.Empty"/> for the conversation itself (keeps the unique index simple).</summary>
    public Guid ThreadKey { get; private set; }

    public string Body { get; private set; }

    public Guid? ReplyToEntryId { get; private set; }

    public long UpdatedTicks { get; private set; }

    public Guid? ThreadRootId => ThreadKey == Guid.Empty ? null : ThreadKey;

    public static MessageDraft Start(Guid id, Guid userId, Guid conversationId, Guid? threadRootId) =>
        new(MessageReaction.RequireId(id), MessageReaction.RequireId(userId), MessageReaction.RequireId(conversationId), threadRootId ?? Guid.Empty);

    /// <summary>Keeps the text as typed (trailing spaces included), up to <paramref name="maxLength"/> characters.</summary>
    public void Update(string body, Guid? replyToEntryId, int maxLength, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Length > maxLength)
        {
            throw new DomainRuleException("message_too_long", "The draft is longer than a message may be.");
        }

        Body = body;
        ReplyToEntryId = replyToEntryId;
        UpdatedTicks = nowUtc.UtcTicks;
    }
}

public enum ScheduledMessageStatus
{
    Pending = 1,

    /// <summary>Could not be sent at its time (owner decision: kept with the reason until the person deletes it).</summary>
    Failed = 2,
}

/// <summary>
/// A message to send later (Phase 20, ADR-034): up to <see cref="MaxDaysAhead"/> days ahead, at most
/// <see cref="MaxPendingPerUser"/> pending per person. Sent through the normal send path at its
/// time, so every rule (access, blocks, attachments, mentions) is checked then; a sent schedule is
/// removed, a failed one keeps its <see cref="FailureCode"/>.
/// </summary>
public sealed class ScheduledMessage
{
    public const int MaxDaysAhead = 30;
    public const int MaxPendingPerUser = 50;

    private ScheduledMessage(Guid id, Guid userId, Guid conversationId)
    {
        Id = id;
        UserId = userId;
        ConversationId = conversationId;
        Body = string.Empty;
        MentionUserIds = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid ConversationId { get; private set; }

    public string Body { get; private set; }

    public Guid? ReplyToEntryId { get; private set; }

    public Guid? ThreadRootId { get; private set; }

    public bool AlsoInConversation { get; private set; }

    /// <summary>Comma-separated user ids mentioned by name (resolved again when sent).</summary>
    public string MentionUserIds { get; private set; }

    public long SendAtTicks { get; private set; }

    public long CreatedTicks { get; private set; }

    public ScheduledMessageStatus Status { get; private set; } = ScheduledMessageStatus.Pending;

    public string? FailureCode { get; private set; }

    public DateTimeOffset SendAtUtc => new(SendAtTicks, TimeSpan.Zero);

    /// <summary>Idempotency key of the message it becomes, so a retried send never posts twice.</summary>
    public string ClientMessageId => $"scheduled:{Id:N}";

    public static ScheduledMessage Create(
        Guid id, Guid userId, Guid conversationId, string body, Guid? replyToEntryId, Guid? threadRootId, bool alsoInConversation,
        IReadOnlyCollection<Guid> mentionUserIds, DateTimeOffset sendAtUtc, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(mentionUserIds);
        var scheduled = new ScheduledMessage(MessageReaction.RequireId(id), MessageReaction.RequireId(userId), MessageReaction.RequireId(conversationId))
        {
            Body = body,
            ReplyToEntryId = replyToEntryId,
            ThreadRootId = threadRootId,
            AlsoInConversation = threadRootId is not null && alsoInConversation,
            MentionUserIds = string.Join(',', mentionUserIds.Distinct()),
            CreatedTicks = nowUtc.UtcTicks,
        };
        scheduled.Reschedule(body, sendAtUtc, nowUtc);
        return scheduled;
    }

    public IReadOnlyList<Guid> MentionedUserIds =>
        MentionUserIds.Length == 0 ? [] : MentionUserIds.Split(',').Select(Guid.Parse).ToList();

    /// <summary>Changes the text and time of a pending schedule.</summary>
    public void Reschedule(string body, DateTimeOffset sendAtUtc, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (Status != ScheduledMessageStatus.Pending)
        {
            throw new DomainRuleException("scheduled_not_pending", "Only messages still waiting to be sent can be changed.");
        }

        if (sendAtUtc <= nowUtc || sendAtUtc > nowUtc.AddDays(MaxDaysAhead))
        {
            throw new DomainRuleException("send_at_invalid", $"Choose a time in the next {MaxDaysAhead} days.");
        }

        Body = body;
        SendAtTicks = sendAtUtc.UtcTicks;
    }

    public void Fail(string code)
    {
        Status = ScheduledMessageStatus.Failed;
        FailureCode = string.IsNullOrWhiteSpace(code) ? "send_failed" : code.Length > 64 ? code[..64] : code;
    }
}

/// <summary>A file sent with a scheduled message; while the schedule exists the upload is kept.</summary>
public sealed class ScheduledMessageFile
{
    private ScheduledMessageFile(Guid scheduledMessageId, Guid attachmentId)
    {
        ScheduledMessageId = scheduledMessageId;
        AttachmentId = attachmentId;
    }

    public Guid ScheduledMessageId { get; private set; }

    public Guid AttachmentId { get; private set; }

    public static ScheduledMessageFile Of(ScheduledMessage scheduled, Guid attachmentId)
    {
        ArgumentNullException.ThrowIfNull(scheduled);
        return new(scheduled.Id, MessageReaction.RequireId(attachmentId));
    }
}

public enum ReminderStatus
{
    Pending = 1,

    /// <summary>Its time came and the person was notified; shown until they mark it done.</summary>
    Due = 2,
}

/// <summary>
/// "Remind me about this message" (Phase 20, ADR-034). At its time the person is told in the app, by
/// push and (when not online) by email; it stays in their reminders until they mark it done.
/// </summary>
public sealed class Reminder
{
    public const int MaxDaysAhead = 365;
    public const int MaxPendingPerUser = 100;

    private Reminder(Guid id, Guid userId, Guid conversationId, Guid entryId, long remindAtTicks)
    {
        Id = id;
        UserId = userId;
        ConversationId = conversationId;
        EntryId = entryId;
        RemindAtTicks = remindAtTicks;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid EntryId { get; private set; }

    public long RemindAtTicks { get; private set; }

    public ReminderStatus Status { get; private set; } = ReminderStatus.Pending;

    public DateTimeOffset RemindAtUtc => new(RemindAtTicks, TimeSpan.Zero);

    public static Reminder Create(Guid id, ConversationEvent message, Guid userId, DateTimeOffset remindAtUtc, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!message.IsMessage || message.IsDeleted)
        {
            throw new DomainRuleException("message_not_found", "Reminders are set on messages.");
        }

        if (remindAtUtc <= nowUtc || remindAtUtc > nowUtc.AddDays(MaxDaysAhead))
        {
            throw new DomainRuleException("remind_at_invalid", $"Choose a time in the next {MaxDaysAhead} days.");
        }

        return new(MessageReaction.RequireId(id), MessageReaction.RequireId(userId), message.ConversationId, message.Id, remindAtUtc.UtcTicks);
    }

    public void BecameDue() => Status = ReminderStatus.Due;
}
