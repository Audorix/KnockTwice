using Audorix.ChatX.Contracts.V1.Users;

namespace Audorix.ChatX.Contracts.V1.Conversations;

/// <param name="Type"><c>direct</c> or <c>group</c>.</param>
/// <param name="Scope"><c>personal</c> or <c>organization</c>.</param>
/// <param name="Title">Group name, or the other person's display name for direct conversations.</param>
/// <param name="Counterpart">The other person of a direct conversation.</param>
/// <param name="MyRole"><c>owner</c>, <c>administrator</c> or <c>member</c> (direct participants are members).</param>
/// <param name="LastSequence">Position of the newest history entry (ADR-022 per-conversation sequence).</param>
/// <param name="LastReadSequence">The caller's read position.</param>
public sealed record ConversationSummaryResponse(
    Guid Id,
    string Type,
    string Scope,
    Guid? OrganizationId,
    string? OrganizationName,
    string Title,
    UserSummaryResponse? Counterpart,
    string MyRole,
    bool Muted,
    long LastSequence,
    long LastReadSequence,
    int UnreadCount,
    DateTimeOffset LastActivityAtUtc,
    Guid? WorkspaceId = null,
    string? Visibility = null,
    string? SupportStatus = null,
    UserSummaryResponse? AssignedTo = null,
    string NotificationLevel = "all");

/// <param name="LastDeliveredSequence">Delivery watermark (ADR-023): one of the person's devices received everything up to here.</param>
/// <param name="LastReadSequence">Read watermark (ADR-023).</param>
public sealed record ConversationParticipantResponse(
    UserSummaryResponse User, string Role, DateTimeOffset JoinedAtUtc, long LastDeliveredSequence, long LastReadSequence);

public sealed record ConversationDetailResponse(
    ConversationSummaryResponse Conversation,
    string? Description,
    IReadOnlyList<ConversationParticipantResponse> Participants);

/// <param name="Kind"><c>message</c>, <c>created</c>, <c>member_added</c>, <c>member_removed</c>, <c>member_left</c>, <c>renamed</c>, <c>description_changed</c> or <c>role_changed</c>.</param>
/// <param name="Actor">Who did it (the author of a message); null if the account no longer exists.</param>
/// <param name="Subject">Whom it concerns (added, removed or role-changed person).</param>
/// <param name="Text">New title or description, or the new role.</param>
/// <param name="Body">Message text (plain, LF line breaks); null for other kinds and deleted messages.</param>
/// <param name="ReplyTo">The quoted message of a reply.</param>
/// <param name="ClientMessageId">The sender's idempotency key (lets the sender match its pending message).</param>
/// <param name="Version">Increases with every edit or deletion; keep the highest.</param>
public sealed record ConversationEntryResponse(
    Guid Id,
    long Sequence,
    string Kind,
    UserSummaryResponse? Actor,
    UserSummaryResponse? Subject,
    string? Text,
    DateTimeOffset CreatedAtUtc,
    string? Body = null,
    ReplyPreviewResponse? ReplyTo = null,
    string? ClientMessageId = null,
    int Version = 1,
    DateTimeOffset? EditedAtUtc = null,
    DateTimeOffset? DeletedAtUtc = null,
    IReadOnlyList<Attachments.MessageAttachmentResponse>? Attachments = null,
    IReadOnlyList<ReactionResponse>? Reactions = null,
    Guid? ThreadRootId = null,
    bool ShownInConversation = false,
    ThreadSummaryResponse? Thread = null,
    MentionsResponse? Mentions = null,
    bool Pinned = false,
    UserSummaryResponse? ForwardedFrom = null,
    bool? Saved = null);

/// <summary>A pinned message (Phase 18).</summary>
public sealed record PinnedMessageResponse(ConversationEntryResponse Entry, UserSummaryResponse? PinnedBy, DateTimeOffset PinnedAtUtc);

public sealed record SetPinnedRequest(bool Pinned);

public sealed record SetSavedRequest(bool Saved);

/// <param name="ClientMessageId">Idempotency key of the copy in the target conversation (1-62 characters).</param>
/// <param name="Comment">Optional text sent just before the forwarded copy.</param>
public sealed record ForwardMessageRequest(Guid TargetConversationId, string? ClientMessageId, string? Comment);

public sealed record SavedItemResponse(Guid ConversationId, string ConversationType, string? ConversationTitle, ConversationEntryResponse Entry, DateTimeOffset SavedAtUtc);

/// <summary>Saved messages, most recently saved first. Pass <paramref name="NextCursor"/> as <c>before</c> for older ones.</summary>
public sealed record SavedPageResponse(IReadOnlyList<SavedItemResponse> Items, string? NextCursor, bool HasMore);

/// <summary>Who a message mentions (Phase 17): people by name, and whether it used <c>@everyone</c> or <c>@here</c>.</summary>
public sealed record MentionsResponse(IReadOnlyList<Guid> UserIds, bool Everyone, bool Here);

/// <param name="Unread">The mentioning message is still unread (in the conversation, or in its thread).</param>
public sealed record MentionItemResponse(Guid ConversationId, string ConversationType, string? ConversationTitle, ConversationEntryResponse Entry, bool Unread);

/// <summary>Mentions of the caller, newest first. Pass <paramref name="NextCursor"/> as <c>before</c> for older ones.</summary>
public sealed record MentionPageResponse(IReadOnlyList<MentionItemResponse> Items, string? NextCursor, bool HasMore);

public sealed record MentionCountResponse(int Unread);

/// <summary>One emoji on a message (Phase 16); <paramref name="UserIds"/> are who reacted, first first.</summary>
public sealed record ReactionResponse(string Emoji, int Count, IReadOnlyList<Guid> UserIds);

/// <summary>A thread on its first message (Phase 16). <paramref name="Following"/> and <paramref name="Unread"/> are the caller's (absent in realtime events).</summary>
public sealed record ThreadSummaryResponse(int ReplyCount, DateTimeOffset? LastReplyAtUtc, bool? Following, int? Unread);

/// <summary>A thread: its first message and a page of replies in ascending order.</summary>
public sealed record ThreadResponse(ConversationEntryResponse Root, IReadOnlyList<ConversationEntryResponse> Items, string? NextCursor, bool HasMore, bool Following, long LastReadSequence);

/// <param name="Reacted">True adds the caller's reaction, false removes it.</param>
public sealed record SetReactionRequest(string? Emoji, bool Reacted);

public sealed record ThreadFollowRequest(bool Following);

public sealed record ThreadReadResponse(int Unread);

/// <param name="Excerpt">Up to 140 characters of the quoted message; null when it was deleted.</param>
public sealed record ReplyPreviewResponse(Guid Id, long Sequence, UserSummaryResponse? Author, string? Excerpt, bool Deleted);

/// <param name="ClientMessageId">Sender-generated id (for example a UUID), 1-64 characters. Retrying with the same id returns the original message; a different body is a 409 <c>idempotency_conflict</c>.</param>
/// <param name="ReplyToId">A message of the same conversation to reply to.</param>
/// <param name="AttachmentIds">Ready attachments the sender uploaded into this conversation (up to 10 by default). With attachments the body may be empty.</param>
/// <param name="ThreadRootId">Phase 16: reply in this message's thread.</param>
/// <param name="AlsoInConversation">With <paramref name="ThreadRootId"/>: also show the reply in the main conversation.</param>
/// <param name="MentionUserIds">Phase 17: participants the message mentions by name (the text should contain their <c>@Name</c>). <c>@everyone</c> and <c>@here</c> are read from the text.</param>
public sealed record SendMessageRequest(
    string? ClientMessageId, string? Body, Guid? ReplyToId, IReadOnlyList<Guid>? AttachmentIds = null, Guid? ThreadRootId = null, bool AlsoInConversation = false,
    IReadOnlyList<Guid>? MentionUserIds = null);

public sealed record EditMessageRequest(string? Body);

public sealed record MarkConversationDeliveredRequest(long Sequence);

public sealed record ConversationDeliveredResponse(long LastDeliveredSequence);

/// <summary>A page of the conversation list (Phase 22), most recent activity first. Pass <paramref name="NextCursor"/> as <c>after</c> for the next page.</summary>
public sealed record ConversationListPageResponse(IReadOnlyList<ConversationSummaryResponse> Items, string? NextCursor, bool HasMore);

/// <summary>A page of history in ascending order. Pass <paramref name="NextCursor"/> as <c>before</c> to load older entries.</summary>
public sealed record ConversationHistoryResponse(IReadOnlyList<ConversationEntryResponse> Items, string? NextCursor, bool HasMore);

/// <param name="OrganizationId">Set for an organization conversation; omit for a personal one.</param>
public sealed record OpenDirectConversationRequest(Guid UserId, Guid? OrganizationId);

public sealed record CreateGroupConversationRequest(string? Title, Guid? OrganizationId, IReadOnlyList<Guid>? UserIds);

/// <summary>Null fields are left unchanged; an empty description clears it.</summary>
public sealed record UpdateConversationRequest(string? Title, string? Description);

public sealed record AddParticipantsRequest(IReadOnlyList<Guid>? UserIds);

/// <param name="Role"><c>owner</c>, <c>administrator</c> or <c>member</c>.</param>
public sealed record ChangeParticipantRoleRequest(string? Role);

public sealed record MarkConversationReadRequest(long Sequence);

public sealed record ConversationReadResponse(long LastReadSequence, int UnreadCount);

/// <param name="NotificationLevel">Phase 17: <c>all</c>, <c>mentions</c> or <c>nothing</c>. Takes precedence over <paramref name="Muted"/>.</param>
/// <param name="Muted">Before Phase 17: true sets <c>mentions</c>, false sets <c>all</c>.</param>
public sealed record ConversationSettingsRequest(bool? Muted = null, string? NotificationLevel = null);
