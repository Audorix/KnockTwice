using Audorix.ChatX.Domain.Organizations;

namespace Audorix.ChatX.Domain.Conversations;

/// <summary>Conversation kinds (Architecture §5.3). Website conversations double as the support conversations of Phase 11.</summary>
public enum ConversationType
{
    Direct = 1,
    Group = 2,

    /// <summary>A website visitor and the widget's responders (Phase 10); worked in the support inbox (Phase 11).</summary>
    Website = 3,

    /// <summary>A named conversation inside a workspace (Phase 11): public (browsable) or private.</summary>
    Channel = 4,
}

public enum ChannelVisibility
{
    /// <summary>Any member of the workspace can find and join it.</summary>
    Public = 1,

    /// <summary>Invitation only; invisible to people outside it (except workspace and organization administrators).</summary>
    Private = 2,
}

/// <summary>Support state of a website conversation (Phase 11).</summary>
public enum SupportStatus
{
    Open = 1,
    Closed = 2,
}

/// <summary>Ownership scope (ADR-021, accepted): personal conversations authorize by participation; organization conversations also need active organization membership.</summary>
public enum ConversationScope
{
    Personal = 1,
    Organization = 2,
}

/// <summary>Per-conversation notification level (Phase 17).</summary>
public enum NotificationLevel
{
    All = 1,
    Mentions = 2,
    Nothing = 3,
}

public enum ParticipantRole
{
    Member = 1,
    Administrator = 2,
    Owner = 3,
}

public enum ParticipantStatus
{
    Active = 1,
    Left = 2,
    Removed = 3,
}

/// <summary>What a history entry records. Phase 6 messages share the same per-conversation sequence.</summary>
public enum ConversationEventKind
{
    Created = 1,
    MemberAdded = 2,
    MemberRemoved = 3,
    MemberLeft = 4,
    Renamed = 5,
    DescriptionChanged = 6,
    RoleChanged = 7,
    Message = 8,

    /// <summary>A support conversation was assigned (subject = assignee) or unassigned (no subject).</summary>
    Assigned = 9,

    /// <summary>A support conversation was closed or reopened (text "closed"/"open").</summary>
    StatusChanged = 10,

    /// <summary>A channel became public or private (text "public"/"private").</summary>
    VisibilityChanged = 11,

    /// <summary>Phase 18: a message was pinned (the entry refers to it like a reply).</summary>
    Pinned = 12,
}

#pragma warning disable CA1711 // "Permission" is the domain term, as for OrganizationPermission.
public enum ConversationPermission
#pragma warning restore CA1711
{
    ManageSettings = 1,
    ManageMembers = 2,
    ManageRoles = 3,
}

/// <summary>
/// A direct or group conversation. <see cref="LastSequence"/> is the per-conversation sequence
/// (ADR-022, accepted): every history entry takes the next number while the row's concurrency
/// token protects the increment, so numbers are unique, ordered and never derived from MAX + 1.
/// Direct conversations carry a <see cref="DirectKey"/> that is unique per pair and scope.
/// </summary>
public sealed class Conversation
{
    public const int TitleMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    private Conversation(
        Guid id, ConversationType type, ConversationScope scope, Guid? organizationId, string? title, string? directKey, Guid createdByUserId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Type = type;
        Scope = scope;
        OrganizationId = organizationId;
        Title = title;
        DirectKey = directKey;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        LastActivityAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public ConversationType Type { get; private set; }

    public ConversationScope Scope { get; private set; }

    /// <summary>Set exactly when <see cref="Scope"/> is <see cref="ConversationScope.Organization"/>.</summary>
    public Guid? OrganizationId { get; private set; }

    /// <summary>Group name; null for direct conversations (shown as the other person's name).</summary>
    public string? Title { get; private set; }

    public string? Description { get; private set; }

    public string? DirectKey { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastActivityAtUtc { get; private set; }

    public long LastSequence { get; private set; }

    /// <summary>Created by the demo data loader; removed with its history when demo data is cleared.</summary>
    public bool IsDemo { get; private set; }

    public void MarkAsDemo() => IsDemo = true;

    /// <summary>The website widget a <see cref="ConversationType.Website"/> conversation came from.</summary>
    public Guid? WidgetId { get; private set; }

    // ---- Channels (Phase 11) -----------------------------------------------------------------

    public const int ChannelNameMaxLength = 80;

    /// <summary>The workspace a channel belongs to.</summary>
    public Guid? WorkspaceId { get; private set; }

    public ChannelVisibility? Visibility { get; private set; }

    /// <summary>Workspace id plus the upper-cased name: unique, so names do not repeat within a workspace.</summary>
    public string? ChannelKey { get; private set; }

    public bool IsChannel => Type == ConversationType.Channel;

    // ---- Support (Phase 11, website conversations) ------------------------------------------

    public SupportStatus? SupportStatus { get; private set; }

    /// <summary>The agent responsible for a website conversation, if any.</summary>
    public Guid? AssignedToUserId { get; private set; }

    /// <summary>An organization conversation between a website visitor and the widget's responders; titled with the visitor's name.</summary>
    public static Conversation CreateWebsite(Guid id, Guid organizationId, Guid widgetId, string visitorName, Guid visitorUserId, DateTimeOffset nowUtc) =>
        new(Require.Id(id), ConversationType.Website, ConversationScope.Organization, Require.Id(organizationId),
            Require.Name(visitorName, TitleMaxLength, "conversation_title"), null, Require.Id(visitorUserId), Require.Utc(nowUtc))
        {
            WidgetId = Require.Id(widgetId),
            SupportStatus = Domain.Conversations.SupportStatus.Open,
        };

    public static Conversation CreateChannel(
        Guid id, Guid organizationId, Guid workspaceId, string name, ChannelVisibility visibility, Guid createdByUserId, DateTimeOffset nowUtc)
    {
        var title = Require.Name(name, ChannelNameMaxLength, "channel_name");
        return new(Require.Id(id), ConversationType.Channel, ConversationScope.Organization, Require.Id(organizationId), title, null,
            Require.Id(createdByUserId), Require.Utc(nowUtc))
        {
            WorkspaceId = Require.Id(workspaceId),
            Visibility = Require.Defined(visibility),
            ChannelKey = ChannelKeyFor(workspaceId, title),
        };
    }

    public static string ChannelKeyFor(Guid workspaceId, string name) => $"{workspaceId:N}:{name.Trim().ToUpperInvariant()}";

    public void SetVisibility(ChannelVisibility visibility)
    {
        RequireChannel();
        Visibility = Require.Defined(visibility);
    }

    public void RequireChannel()
    {
        if (!IsChannel)
        {
            throw new DomainRuleException("not_a_channel", "This operation is only available for channels.");
        }
    }

    public void RequireWebsite()
    {
        if (Type != ConversationType.Website)
        {
            throw new DomainRuleException("not_a_support_conversation", "This operation is only available for website conversations.");
        }
    }

    /// <summary>Assigns the support conversation (null: unassigned). True when it changed.</summary>
    public bool Assign(Guid? userId)
    {
        RequireWebsite();
        if (AssignedToUserId == userId)
        {
            return false;
        }

        AssignedToUserId = userId;
        return true;
    }

    /// <summary>Closes or reopens the support conversation. True when it changed.</summary>
    public bool SetSupportStatus(SupportStatus status)
    {
        RequireWebsite();
        if (SupportStatus == Require.Defined(status))
        {
            return false;
        }

        SupportStatus = status;
        return true;
    }

    /// <summary>Keeps a website conversation's title in line with the visitor's (verified) name.</summary>
    public void RenameVisitor(string visitorName)
    {
        if (Type == ConversationType.Website)
        {
            Title = Require.Name(visitorName, TitleMaxLength, "conversation_title");
        }
    }

    public static Conversation CreateDirect(Guid id, ConversationScope scope, Guid? organizationId, Guid userA, Guid userB, DateTimeOffset nowUtc)
    {
        if (Require.Id(userA) == Require.Id(userB))
        {
            throw new DomainRuleException("cannot_target_self", "A direct conversation needs two different people.");
        }

        return new(Require.Id(id), ConversationType.Direct, Require.Defined(scope), ScopeOrganization(scope, organizationId), null,
            DirectKeyFor(scope, organizationId, userA, userB), userA, Require.Utc(nowUtc));
    }

    public static Conversation CreateGroup(Guid id, ConversationScope scope, Guid? organizationId, string title, Guid createdByUserId, DateTimeOffset nowUtc) =>
        new(Require.Id(id), ConversationType.Group, Require.Defined(scope), ScopeOrganization(scope, organizationId),
            Require.Name(title, TitleMaxLength, "conversation_title"), null, Require.Id(createdByUserId), Require.Utc(nowUtc));

    /// <summary>Order-independent key: one direct conversation per pair of people per scope (ADR-021).</summary>
    public static string DirectKeyFor(ConversationScope scope, Guid? organizationId, Guid userA, Guid userB)
    {
        var (low, high) = userA.CompareTo(userB) < 0 ? (userA, userB) : (userB, userA);
        var owner = scope == ConversationScope.Organization ? $"org:{organizationId:N}" : "personal";
        return $"{owner}:{low:N}:{high:N}";
    }

    /// <summary>Takes the next history position and records activity.</summary>
    public long NextSequence(DateTimeOffset nowUtc)
    {
        LastSequence++;
        LastActivityAtUtc = Require.Utc(nowUtc);
        return LastSequence;
    }

    public void Rename(string title)
    {
        if (IsChannel)
        {
            Title = Require.Name(title, ChannelNameMaxLength, "channel_name");
            ChannelKey = ChannelKeyFor(WorkspaceId!.Value, Title);
            return;
        }

        RequireGroup();
        Title = Require.Name(title, TitleMaxLength, "conversation_title");
    }

    public void Describe(string? description)
    {
        if (!IsChannel)
        {
            RequireGroup();
        }

        var trimmed = description?.Trim() ?? string.Empty;
        if (trimmed.Length > DescriptionMaxLength)
        {
            throw new DomainRuleException("conversation_description_too_long", $"The description may contain at most {DescriptionMaxLength} characters.");
        }

        Description = trimmed.Length == 0 ? null : trimmed;
    }

    public void RequireGroup()
    {
        if (Type != ConversationType.Group)
        {
            throw new DomainRuleException("not_a_group", "This operation is only available for group conversations.");
        }
    }

    private static Guid? ScopeOrganization(ConversationScope scope, Guid? organizationId) => scope switch
    {
        ConversationScope.Organization when organizationId is { } id && id != Guid.Empty => id,
        ConversationScope.Personal when organizationId is null => null,
        _ => throw new DomainRuleException("conversation_scope_invalid", "Organization conversations need an organization; personal ones must not have one."),
    };
}

/// <summary>
/// A user's participation. Unique per conversation/user; leaving or removal keeps the row and
/// ends access, and rejoining reactivates it with a new history start. History is visible from
/// <see cref="VisibleFromSequence"/> (the entry that recorded the join) onwards.
/// <see cref="LastReadSequence"/> only moves forward.
/// </summary>
public sealed class ConversationParticipant
{
    private ConversationParticipant(Guid id, Guid conversationId, Guid userId, ParticipantRole role, long visibleFromSequence, DateTimeOffset joinedAtUtc)
    {
        Id = id;
        ConversationId = conversationId;
        UserId = userId;
        Role = role;
        Status = ParticipantStatus.Active;
        JoinedAtUtc = joinedAtUtc;
        VisibleFromSequence = visibleFromSequence;
        LastReadSequence = visibleFromSequence - 1;
        LastDeliveredSequence = visibleFromSequence - 1;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Delivery watermark (ADR-023): the highest position one of the participant's devices
    /// acknowledged. Moves forward only, never past the conversation's last position, and is at
    /// least the read position.
    /// </summary>
    public long LastDeliveredSequence { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid UserId { get; private set; }

    public ParticipantRole Role { get; private set; }

    public ParticipantStatus Status { get; private set; }

    public DateTimeOffset JoinedAtUtc { get; private set; }

    public DateTimeOffset? LeftAtUtc { get; private set; }

    public long VisibleFromSequence { get; private set; }

    public long LastReadSequence { get; private set; }

    /// <summary>
    /// What the person is notified about in this conversation (Phase 17, ADR-031): every message,
    /// only messages that mention them, or nothing. Mentions break through "only mentions"; "nothing"
    /// silences them too.
    /// </summary>
    public NotificationLevel NotificationLevel { get; private set; } = NotificationLevel.All;

    /// <summary>Not notified about every message (the former "muted").</summary>
    public bool Muted => NotificationLevel != NotificationLevel.All;

    /// <summary>
    /// Notifications (Phase 15): the highest position the person was already told about by email,
    /// so an unread message is emailed once. Moves forward only.
    /// </summary>
    public long LastNotifiedSequence { get; private set; }

    public void MarkNotified(long sequence)
    {
        if (sequence > LastNotifiedSequence)
        {
            LastNotifiedSequence = sequence;
        }
    }

    /// <summary>
    /// A website visitor (Phase 10): takes part in an organization conversation without being a
    /// member of the organization. Visitor participation never grants anything else.
    /// </summary>
    public bool IsVisitor { get; private set; }

    /// <summary>
    /// A guest of the organization (Phase 11): takes part in a channel through an active
    /// <see cref="OrganizationGuest"/> record instead of organization membership. Never a manager.
    /// </summary>
    public bool IsGuest { get; private set; }

    public bool IsActive => Status == ParticipantStatus.Active;

    public static ConversationParticipant JoinGuest(Guid id, Guid conversationId, Guid guestUserId, long visibleFromSequence, DateTimeOffset nowUtc) =>
        new(Require.Id(id), Require.Id(conversationId), Require.Id(guestUserId), ParticipantRole.Member, RequireSequence(visibleFromSequence), Require.Utc(nowUtc))
        {
            IsGuest = true,
        };

    public static ConversationParticipant Join(Guid id, Guid conversationId, Guid userId, ParticipantRole role, long visibleFromSequence, DateTimeOffset nowUtc) =>
        new(Require.Id(id), Require.Id(conversationId), Require.Id(userId), Require.Defined(role), RequireSequence(visibleFromSequence), Require.Utc(nowUtc));

    public static ConversationParticipant JoinVisitor(Guid id, Guid conversationId, Guid visitorUserId, long visibleFromSequence, DateTimeOffset nowUtc) =>
        new(Require.Id(id), Require.Id(conversationId), Require.Id(visitorUserId), ParticipantRole.Member, RequireSequence(visibleFromSequence), Require.Utc(nowUtc))
        {
            IsVisitor = true,
        };

    public void Rejoin(ParticipantRole role, long visibleFromSequence, DateTimeOffset nowUtc, bool asGuest = false)
    {
        if (IsActive)
        {
            throw new DomainRuleException("already_participant", "The user already takes part in this conversation.");
        }

        if (asGuest && role != ParticipantRole.Member)
        {
            throw new DomainRuleException("value_invalid", "Guests are always plain members.");
        }

        IsGuest = asGuest;
        Status = ParticipantStatus.Active;
        Role = Require.Defined(role);
        JoinedAtUtc = Require.Utc(nowUtc);
        LeftAtUtc = null;
        VisibleFromSequence = RequireSequence(visibleFromSequence);
        LastReadSequence = visibleFromSequence - 1;
        LastDeliveredSequence = visibleFromSequence - 1;
        NotificationLevel = NotificationLevel.All;
    }

    public void Leave(DateTimeOffset nowUtc) => End(ParticipantStatus.Left, nowUtc);

    public void Remove(DateTimeOffset nowUtc) => End(ParticipantStatus.Removed, nowUtc);

    public void ChangeRole(ParticipantRole role)
    {
        RequireActive();
        if (IsGuest && role != ParticipantRole.Member)
        {
            throw new DomainRuleException("guest_role_fixed", "Guests cannot manage conversations.");
        }

        Role = Require.Defined(role);
    }

    /// <summary>Moves the read position forward, never past <paramref name="lastSequence"/> or before the visible history.</summary>
    public bool MarkRead(long sequence, long lastSequence)
    {
        RequireActive();
        var target = Math.Min(sequence, lastSequence);
        if (target <= LastReadSequence)
        {
            return false;
        }

        LastReadSequence = target;
        LastDeliveredSequence = Math.Max(LastDeliveredSequence, target); // reading implies delivery
        return true;
    }

    /// <summary>Moves the delivery watermark forward (never backwards, never past <paramref name="lastSequence"/>).</summary>
    public bool MarkDelivered(long sequence, long lastSequence)
    {
        RequireActive();
        var target = Math.Min(sequence, lastSequence);
        if (target <= LastDeliveredSequence)
        {
            return false;
        }

        LastDeliveredSequence = target;
        return true;
    }

    public void SetNotificationLevel(NotificationLevel level)
    {
        RequireActive();
        NotificationLevel = Require.Defined(level);
    }

    /// <summary>Whether a message, mentioning the person or not, should notify them.</summary>
    public bool WantsNotification(bool mentioned) => NotificationLevel switch
    {
        NotificationLevel.All => true,
        NotificationLevel.Mentions => mentioned,
        _ => false,
    };

    public bool Can(ConversationPermission permission) => IsActive && (permission, Role) switch
    {
        (_, ParticipantRole.Owner) => true,
        (ConversationPermission.ManageSettings or ConversationPermission.ManageMembers, ParticipantRole.Administrator) => true,
        _ => false,
    };

    private void End(ParticipantStatus status, DateTimeOffset nowUtc)
    {
        RequireActive();
        Status = status;
        LeftAtUtc = Require.Utc(nowUtc);
    }

    private void RequireActive() => Require.ActiveMembership(IsActive);

    private static long RequireSequence(long sequence) =>
        sequence >= 1 ? sequence : throw new DomainRuleException("value_invalid", "History positions start at 1.");
}

/// <summary>An immutable history entry at a unique (conversation, sequence) position.</summary>
public sealed class ConversationEvent
{
    public const int TextMaxLength = Conversation.DescriptionMaxLength;

    private ConversationEvent(
        Guid id, Guid conversationId, long sequence, ConversationEventKind kind, Guid actorUserId, Guid? subjectUserId, string? text, DateTimeOffset createdAtUtc)
    {
        Id = id;
        ConversationId = conversationId;
        Sequence = sequence;
        Kind = kind;
        ActorUserId = actorUserId;
        SubjectUserId = subjectUserId;
        Text = text;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public long Sequence { get; private set; }

    public ConversationEventKind Kind { get; private set; }

    public Guid ActorUserId { get; private set; }

    public Guid? SubjectUserId { get; private set; }

    /// <summary>New title or description, or the new role name for role changes.</summary>
    public string? Text { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    // ---- Message entries (Phase 6) -----------------------------------------------------------

    /// <summary>Message text (plain text, LF line breaks); null for other kinds and for deleted messages.</summary>
    public string? Body { get; private set; }

    /// <summary>The message this one replies to (same conversation, enforced by a composite foreign key).</summary>
    public Guid? ReplyToEntryId { get; private set; }

    /// <summary>Sender-chosen idempotency key, unique per conversation and sender for ever.</summary>
    public string? ClientMessageId { get; private set; }

    /// <summary>Starts at 1 and increases with every edit or deletion; clients keep the highest version.</summary>
    public int Version { get; private set; } = 1;

    public DateTimeOffset? EditedAtUtc { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public Guid? DeletedByUserId { get; private set; }

    public bool IsMessage => Kind == ConversationEventKind.Message;

    public bool IsDeleted => DeletedAtUtc is not null;

    // ---- Threads (Phase 16) ------------------------------------------------------------------

    /// <summary>The message this one answers in a thread (owner decision: side panel); null in the main conversation.</summary>
    public Guid? ThreadRootId { get; private set; }

    /// <summary>A thread reply also shown in the main conversation ("also send to the conversation").</summary>
    public bool ShownInConversation { get; private set; }

    /// <summary>On a thread's first message: how many replies it has (deleted ones not counted).</summary>
    public int ThreadReplyCount { get; private set; }

    public DateTimeOffset? ThreadLastReplyAtUtc { get; private set; }

    public bool IsThreadReply => ThreadRootId is not null;

    /// <summary>Whether the entry belongs in the main timeline (everything except thread replies that are not echoed).</summary>
    public bool InMainTimeline => ThreadRootId is null || ShownInConversation;

    // ---- Message tools (Phase 18) ------------------------------------------------------------

    /// <summary>A forwarded copy: who wrote the original (owner decision: credit, no link back).</summary>
    public Guid? ForwardedFromUserId { get; private set; }

    public void MarkForwarded(Guid originalAuthorUserId)
    {
        if (!IsMessage)
        {
            throw new DomainRuleException("value_invalid", "Only messages can be forwarded copies.");
        }

        ForwardedFromUserId = Require.Id(originalAuthorUserId);
    }

    /// <summary>Records that <paramref name="actorUserId"/> pinned <paramref name="message"/> (referred to like a reply, so a deletion shows).</summary>
    public static ConversationEvent RecordPinned(Guid id, Conversation conversation, Guid actorUserId, ConversationEvent message, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(message);
        var entry = Record(id, conversation, ConversationEventKind.Pinned, actorUserId, null, null, nowUtc);
        entry.ReplyToEntryId = message.ConversationId == entry.ConversationId && message.IsMessage
            ? message.Id
            : throw new DomainRuleException("message_not_found", "Only messages of the conversation can be pinned.");
        return entry;
    }

    /// <summary>Makes a newly posted message a reply in <paramref name="root"/>'s thread (one level: no threads in threads).</summary>
    public void PlaceInThread(ConversationEvent root, bool alsoInConversation)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!IsMessage || root.ConversationId != ConversationId || !root.IsMessage || root.IsDeleted || root.IsThreadReply || root.Id == Id)
        {
            throw new DomainRuleException("thread_root_invalid", "Threads start from a message of the same conversation that is not itself a thread reply.");
        }

        ThreadRootId = root.Id;
        ShownInConversation = alsoInConversation;
        root.ThreadReplyCount++;
        root.ThreadLastReplyAtUtc = CreatedAtUtc;
    }

    /// <summary>A reply of this thread was deleted.</summary>
    public void ThreadReplyRemoved() => ThreadReplyCount = Math.Max(0, ThreadReplyCount - 1);

    /// <summary>The thread's first message disappeared (retention, removed account): the reply becomes an ordinary message.</summary>
    public void LeaveThread()
    {
        ThreadRootId = null;
        ShownInConversation = false;
    }

    /// <summary>Records the next entry of the conversation, taking its sequence.</summary>
    public static ConversationEvent Record(
        Guid id, Conversation conversation, ConversationEventKind kind, Guid actorUserId, Guid? subjectUserId, string? text, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (kind == ConversationEventKind.Message)
        {
            throw new DomainRuleException("value_invalid", "Use Post for messages.");
        }

        if (text is { Length: > TextMaxLength })
        {
            text = text[..TextMaxLength];
        }

        return new(Require.Id(id), conversation.Id, conversation.NextSequence(nowUtc), Require.Defined(kind), Require.Id(actorUserId), subjectUserId, text, Require.Utc(nowUtc));
    }

    /// <summary>Posts a message at the next position. A reply must target a message of the same conversation that is not deleted.</summary>
    public static ConversationEvent Post(
        Guid id, Conversation conversation, Guid authorUserId, string clientMessageId, string body, ConversationEvent? replyTo, int maxLength, DateTimeOffset nowUtc,
        bool allowEmptyBody = false)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        var key = clientMessageId?.Trim() ?? string.Empty;
        if (key.Length is 0 or > ClientMessageIdMaxLength || key.Any(char.IsControl))
        {
            throw new DomainRuleException("client_message_id_invalid", "A client message id of 1-64 characters is required.");
        }

        if (replyTo is not null && (replyTo.ConversationId != conversation.Id || !replyTo.IsMessage || replyTo.IsDeleted))
        {
            throw new DomainRuleException("reply_target_invalid", "Replies must refer to an existing message of the same conversation.");
        }

        var text = NormalizeBody(body, maxLength, allowEmptyBody);
        var entry = new ConversationEvent(Require.Id(id), conversation.Id, conversation.NextSequence(nowUtc), ConversationEventKind.Message,
            Require.Id(authorUserId), null, null, Require.Utc(nowUtc))
        {
            Body = text,
            ClientMessageId = key,
            ReplyToEntryId = replyTo?.Id,
        };
        return entry;
    }

    /// <summary>The author edits their message within <paramref name="window"/> of posting it.</summary>
    public void Edit(Guid userId, string body, TimeSpan window, int maxLength, DateTimeOffset nowUtc, bool allowEmptyBody = false)
    {
        RequireLiveMessage();
        if (userId != ActorUserId)
        {
            throw new DomainRuleException("message_not_editable", "Only the author can edit a message.");
        }

        if (nowUtc - CreatedAtUtc > window)
        {
            throw new DomainRuleException("message_edit_window_expired", "The message can no longer be edited.");
        }

        var text = NormalizeBody(body, maxLength, allowEmptyBody);
        if (text == Body)
        {
            return;
        }

        Body = text;
        EditedAtUtc = Require.Utc(nowUtc);
        Version++;
    }

    /// <summary>Deletes the message, keeping a tombstone at its position. Permission is checked by the caller.</summary>
    public void Delete(Guid byUserId, DateTimeOffset nowUtc)
    {
        RequireLiveMessage();
        Body = null;
        DeletedAtUtc = Require.Utc(nowUtc);
        DeletedByUserId = Require.Id(byUserId);
        Version++;
    }

    public const int ClientMessageIdMaxLength = 64;

    /// <summary>Same idempotency key and same content (and reply target): the retry of the original send.</summary>
    public bool IsSameSend(string body, Guid? replyToEntryId, int maxLength, bool allowEmptyBody = false)
    {
        string normalized;
        try
        {
            normalized = NormalizeBody(body, maxLength, allowEmptyBody);
        }
        catch (DomainRuleException)
        {
            return false;
        }

        return IsMessage && !IsDeleted && Body == normalized && ReplyToEntryId == replyToEntryId;
    }

    /// <summary>
    /// Plain text: CRLF/CR become LF, trailing whitespace trimmed, no control characters except LF
    /// and tab. An empty body is allowed only for messages that carry attachments.
    /// </summary>
    public static string NormalizeBody(string? body, int maxLength, bool allowEmpty = false)
    {
        var text = (body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd();
        text = text.TrimStart('\n');
        if (text.Trim().Length == 0)
        {
            if (allowEmpty)
            {
                return string.Empty;
            }

            throw new DomainRuleException("message_body_required", "A message needs some text.");
        }

        if (text.Length > maxLength)
        {
            throw new DomainRuleException("message_body_too_long", $"A message may contain at most {maxLength} characters.");
        }

        if (text.Any(c => char.IsControl(c) && c is not '\n' and not '\t'))
        {
            throw new DomainRuleException("message_body_invalid", "The message contains invalid characters.");
        }

        return text;
    }

    private void RequireLiveMessage()
    {
        if (!IsMessage || IsDeleted)
        {
            throw new DomainRuleException("message_not_found", "The message does not exist or was deleted.");
        }
    }
}
