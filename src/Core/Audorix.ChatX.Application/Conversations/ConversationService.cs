using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Contacts;
using Audorix.ChatX.Application.Organizations;
using Audorix.ChatX.Application.Realtime;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Domain.Contacts;
using Audorix.ChatX.Domain.Conversations;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Conversations;

/// <summary>Persistence port for conversations, participants and history entries.</summary>
public interface IConversationStore
{
    void Add(Conversation conversation);

    void Add(ConversationParticipant participant);

    void Add(ConversationEvent entry);

    Task<Conversation?> FindAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<Conversation?> FindDirectAsync(string directKey, CancellationToken cancellationToken);

    Task<ConversationParticipant?> FindParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationParticipant>> ListActiveParticipantsAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>The user's active participations with unread counts and, for direct conversations, the other person.</summary>
    Task<IReadOnlyList<Participation>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Entries after <paramref name="afterSequence"/> written by someone other than <paramref name="userId"/>.</summary>
    Task<int> CountUnreadAsync(Guid conversationId, Guid userId, long afterSequence, CancellationToken cancellationToken);

    /// <summary>Entries with <paramref name="fromSequence"/> ≤ sequence &lt; <paramref name="beforeSequence"/>, newest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<ConversationEvent>> ListHistoryAsync(
        Guid conversationId, long fromSequence, long beforeSequence, int limit, CancellationToken cancellationToken);

    /// <summary>Active participations of the user in conversations owned by the organization.</summary>
    Task<IReadOnlyList<Guid>> ListActiveConversationIdsInOrganizationAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    // ---- Messaging (Phase 6) ---------------------------------------------------------------

    void Add(Messaging.OutboxMessage message);

    void Remove(Messaging.OutboxMessage message);

    Task<ConversationEvent?> FindEntryAsync(Guid conversationId, Guid entryId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationEvent>> FindEntriesAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);

    Task<ConversationEvent?> FindMessageByClientIdAsync(Guid conversationId, Guid authorUserId, string clientMessageId, CancellationToken cancellationToken);

    /// <summary>
    /// Users who may receive realtime events about position <paramref name="sequence"/> right now:
    /// active participants whose visible history includes it and, for organization conversations,
    /// active members of the organization.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListRecipientsAsync(Guid conversationId, long sequence, CancellationToken cancellationToken);

    Task<Messaging.OutboxMessage?> FindOutboxAsync(Guid outboxId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListDueOutboxAsync(DateTimeOffset nowUtc, int limit, CancellationToken cancellationToken);

    // ---- Channels and support (Phase 11) ------------------------------------------------------

    Task<IReadOnlyList<Conversation>> ListChannelsAsync(Guid workspaceId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> CountActiveParticipantsAsync(IReadOnlyCollection<Guid> conversationIds, CancellationToken cancellationToken);

    /// <summary>Channels of the workspace in which the user takes part.</summary>
    Task<IReadOnlyList<Guid>> ListActiveChannelIdsInWorkspaceAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Website conversations of the organization, most recent activity first.</summary>
    Task<IReadOnlyList<Conversation>> ListWebsiteConversationsAsync(Guid organizationId, SupportStatus? status, int limit, CancellationToken cancellationToken);

    // ---- Reactions and threads (Phase 16) -----------------------------------------------------

    void Add(MessageReaction reaction);

    void Remove(MessageReaction reaction);

    Task<MessageReaction?> FindReactionAsync(Guid entryId, Guid userId, string emoji, CancellationToken cancellationToken);

    Task<IReadOnlyList<MessageReaction>> ListReactionsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);

    void Add(ThreadFollow follow);

    Task<ThreadFollow?> FindThreadFollowAsync(Guid rootId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ThreadFollow>> ListThreadFollowsAsync(Guid userId, IReadOnlyCollection<Guid> rootIds, CancellationToken cancellationToken);

    /// <summary>People following the thread (they are notified about its replies).</summary>
    Task<IReadOnlyList<Guid>> ListThreadFollowerIdsAsync(Guid rootId, CancellationToken cancellationToken);

    /// <summary>Unread replies (by others, not deleted) per thread for the given follows.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountThreadUnreadAsync(Guid userId, IReadOnlyCollection<ThreadFollow> follows, CancellationToken cancellationToken);

    /// <summary>Replies of a thread with <paramref name="fromSequence"/> ≤ sequence &lt; <paramref name="beforeSequence"/>, newest first.</summary>
    Task<IReadOnlyList<ConversationEvent>> ListThreadAsync(
        Guid conversationId, Guid rootId, long fromSequence, long beforeSequence, int limit, CancellationToken cancellationToken);

    // ---- Mentions (Phase 17) ------------------------------------------------------------------

    void Add(MessageMention mention);

    void Remove(MessageMention mention);

    Task<IReadOnlyList<MessageMention>> ListMentionsForEntryAsync(Guid entryId, CancellationToken cancellationToken);

    /// <summary>Who the entries mention: named people per entry, and which entries used @everyone / @here.</summary>
    Task<IReadOnlyDictionary<Guid, MentionsView>> DescribeMentionsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);

    /// <summary>Everyone the entry mentions (named or through @everyone / @here).</summary>
    Task<IReadOnlyList<Guid>> ListMentionedUserIdsAsync(Guid entryId, CancellationToken cancellationToken);

    /// <summary>
    /// Files shared in the conversation (redesign 2026-11): ready attachments of messages that are not
    /// deleted, at or after <paramref name="visibleFromSequence"/> and before
    /// <paramref name="beforeSequence"/>, newest message first; at most <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<(Domain.Attachments.Attachment Attachment, ConversationEvent Entry)>> ListSharedFilesAsync(
        Guid conversationId, long visibleFromSequence, long? beforeSequence, int limit, CancellationToken cancellationToken);

    /// <summary>The newest main-timeline message (thread replies only when shown in the conversation) of each conversation.</summary>
    Task<IReadOnlyDictionary<Guid, ConversationEvent>> FindLastMessagesAsync(IReadOnlyCollection<Guid> conversationIds, CancellationToken cancellationToken);

    /// <summary>Phase 24: the people mentioned in each of several messages, in one query (messages without mentions are absent).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListMentionedUserIdsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);

    /// <summary>
    /// Mentions of the user in messages they can see (active participation, visible history, not
    /// deleted) sent before <paramref name="beforeTicks"/>, newest first.
    /// </summary>
    Task<IReadOnlyList<MentionRecord>> ListMentionsForUserAsync(Guid userId, long beforeTicks, int limit, CancellationToken cancellationToken);

    Task<int> CountUnreadMentionsAsync(Guid userId, CancellationToken cancellationToken);

    // ---- Message tools (Phase 18) -------------------------------------------------------------

    void Add(MessagePin pin);

    void Remove(MessagePin pin);

    Task<MessagePin?> FindPinAsync(Guid entryId, CancellationToken cancellationToken);

    Task<int> CountPinsAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>The conversation's pins, most recently pinned first.</summary>
    Task<IReadOnlyList<MessagePin>> ListPinsAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListPinnedEntryIdsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);

    void Add(SavedMessage saved);

    void Remove(SavedMessage saved);

    Task<SavedMessage?> FindSavedAsync(Guid userId, Guid entryId, CancellationToken cancellationToken);

    Task<int> CountSavedAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListSavedEntryIdsAsync(Guid userId, IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);

    /// <summary>Everyone's saved rows and the pin of one message (removed with it).</summary>
    Task<(IReadOnlyList<SavedMessage> Saved, MessagePin? Pin)> ListToolsForEntryAsync(Guid entryId, CancellationToken cancellationToken);

    /// <summary>
    /// The user's saved messages they can still see (active participation, visible history, not
    /// deleted) saved before <paramref name="beforeTicks"/>, most recent first.
    /// </summary>
    Task<IReadOnlyList<SavedRecord>> ListSavedForUserAsync(Guid userId, long beforeTicks, int limit, CancellationToken cancellationToken);

    /// <summary>Saves; false (with pending changes discarded) when a unique index or the sequence token rejected the write.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Phase 24: starts a transaction that holds the conversation's row, and re-reads the tracked
    /// <paramref name="conversation"/>, so the next position can be taken without racing other
    /// writers: concurrent sends to the same conversation wait for each other instead of failing.
    /// Saves inside it take part in the transaction; <see cref="IConversationAppendLock.CommitAsync"/>
    /// makes them durable, disposing without committing rolls them back.
    /// </summary>
    Task<IConversationAppendLock> LockForAppendAsync(Conversation conversation, CancellationToken cancellationToken);
}

/// <summary>The row lock taken by <see cref="IConversationStore.LockForAppendAsync"/>.</summary>
public interface IConversationAppendLock : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public sealed record Participation(Conversation Conversation, ConversationParticipant Participant, int UnreadCount, Guid? CounterpartUserId);

public sealed class ConversationSettings
{
    public const string SectionName = "Conversations";

    /// <summary>Upper bound for active participants of a group, including the owner.</summary>
    public int MaxGroupParticipants { get; set; } = 256;
}

public sealed record ConversationSummaryView(
    Conversation Conversation, ConversationParticipant Me, int UnreadCount, UserSummary? Counterpart, string? OrganizationName,
    LastMessageView? LastMessage = null);

/// <summary>A file shared in a conversation, with the message that carried it (redesign 2026-11).</summary>
public sealed record SharedFileView(Attachments.AttachmentView File, ConversationEvent Entry, UserSummary? Author);

/// <param name="NextBefore">Pass as the next page's "before" position; null on the last page.</param>
public sealed record SharedFilesPage(IReadOnlyList<SharedFileView> Items, long? NextBefore);

/// <summary>The newest visible main-timeline message of a conversation, for the list (redesign 2026-11).</summary>
public sealed record LastMessageView(ConversationEvent Entry, UserSummary? Author);

/// <summary>Where a page of the conversation list ends: the last item's activity time and id (Phase 22).</summary>
public readonly record struct ConversationListPosition(long ActivityTicks, Guid Id)
{
    /// <summary>True when this position comes after <paramref name="other"/> in list order (older activity, then lower id).</summary>
    public bool IsAfter(ConversationListPosition other) =>
        ActivityTicks < other.ActivityTicks || (ActivityTicks == other.ActivityTicks && Id.CompareTo(other.Id) < 0);
}

/// <summary>A page of the conversation list; <see cref="Next"/> is set when more follow.</summary>
public sealed record ConversationListPage(IReadOnlyList<ConversationSummaryView> Items, ConversationListPosition? Next);

public sealed record ParticipantView(ConversationParticipant Participant, UserSummary User);

public sealed record ConversationDetailView(ConversationSummaryView Summary, IReadOnlyList<ParticipantView> Participants);

/// <summary>A quoted message a reply refers to; <see cref="Excerpt"/> is null when it was deleted.</summary>
public sealed record ReplyPreview(Guid Id, long Sequence, UserSummary? Author, string? Excerpt, bool Deleted);

public sealed record HistoryEntryView(
    ConversationEvent Entry, UserSummary? Actor, UserSummary? Subject, ReplyPreview? ReplyTo = null, IReadOnlyList<Attachments.AttachmentView>? Attachments = null,
    IReadOnlyList<ReactionView>? Reactions = null, ThreadView? Thread = null, MentionsView? Mentions = null,
    bool Pinned = false, UserSummary? ForwardedFrom = null, bool? Saved = null);

/// <summary>One emoji on a message with who reacted (Phase 16).</summary>
public sealed record ReactionView(string Emoji, IReadOnlyList<Guid> UserIds);

/// <summary>
/// A thread's summary on its first message (Phase 16). <paramref name="Following"/> and
/// <paramref name="Unread"/> are the caller's, and only set in responses to that caller.
/// </summary>
public sealed record ThreadView(int ReplyCount, DateTimeOffset? LastReplyAtUtc, bool? Following = null, int? Unread = null);

/// <param name="NextBeforeSequence">Pass as the next page's upper bound; null when <paramref name="HasMore"/> is false.</param>
public sealed record HistoryPage(IReadOnlyList<HistoryEntryView> Items, long? NextBeforeSequence, bool HasMore);

/// <summary>Realtime notification that a conversation changed; clients reload what they show.</summary>
/// <param name="Change"><c>created</c>, <c>updated</c>, <c>members</c>, <c>read</c> or <c>removed</c>.</param>
public sealed record ConversationUpdated(Guid ConversationId, string Change, long LastSequence) : IRealtimeEvent;

/// <summary>
/// Conversation use cases (Phase 5). Every operation resolves the caller's active participation
/// server-side (plus active organization membership for organization conversations, ADR-021);
/// anything else is "conversation not found". Owner decisions recorded 2026-11-22:
/// <list type="bullet">
/// <item>History is visible from the entry that recorded the join; people who leave or are removed lose access.</item>
/// <item>Direct conversations: between contacts (personal) or active members of the organization; one per pair and scope; never across a block.</item>
/// <item>Groups: owner/administrator/member; personal groups add the adder's contacts, organization groups add active members; nobody can add someone they blocked or who blocked them; if the last owner leaves, the longest-standing member becomes owner.</item>
/// <item>Ordering (ADR-022): a per-conversation sequence for history cursors and unread counts.</item>
/// </list>
/// Changes are saved before realtime notifications are published (ADR-012).
/// </summary>
public sealed partial class ConversationService(
    IConversationStore store,
    IOrganizationStore organizations,
    IContactStore contacts,
    IUserDirectory directory,
    IRealtimePublisher publisher,
    RealtimeService realtime,
    TimeProvider time,
    IOptionsSnapshot<ConversationSettings> settings,
    Attachments.IAttachmentStore attachments)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    private const int SaveAttempts = 3;
    private const string NotFound = "conversation_not_found";

    // ---- Creation --------------------------------------------------------------------------

    /// <summary>Opens the direct conversation with another person, creating it on first use.</summary>
    public async Task<(ConversationSummaryView View, bool Created)> OpenDirectAsync(
        Guid userId, Guid otherUserId, Guid? organizationId, CancellationToken cancellationToken)
    {
        if (otherUserId == userId)
        {
            throw new RequestValidationException("userId", "cannot_target_self");
        }

        var scope = organizationId is null ? ConversationScope.Personal : ConversationScope.Organization;
        if (organizationId is { } orgId)
        {
            await RequireOrganizationMemberAsync(userId, orgId, cancellationToken).ConfigureAwait(false);
        }

        if (!await MayIncludeAsync(userId, otherUserId, organizationId, cancellationToken).ConfigureAwait(false))
        {
            throw new ResourceNotFoundException("user_not_found");
        }

        var key = Conversation.DirectKeyFor(scope, organizationId, userId, otherUserId);
        for (var attempt = 1; ; attempt++)
        {
            var now = time.GetUtcNow();
            if (await store.FindDirectAsync(key, cancellationToken).ConfigureAwait(false) is { } existing)
            {
                // A participant removed meanwhile (e.g. after leaving the organization) comes back
                // with history starting now; nothing earlier becomes visible again.
                var returning = false;
                foreach (var personId in new[] { userId, otherUserId })
                {
                    if (await store.FindParticipantAsync(existing.Id, personId, cancellationToken).ConfigureAwait(false) is { IsActive: false } inactive)
                    {
                        var added = ConversationEvent.Record(Guid.CreateVersion7(now), existing, ConversationEventKind.MemberAdded, userId, personId, null, now);
                        store.Add(added);
                        inactive.Rejoin(ParticipantRole.Member, added.Sequence, now);
                        returning = true;
                    }
                }

                if (!returning)
                {
                    return (await SummaryAsync(userId, existing, cancellationToken).ConfigureAwait(false), false);
                }

                if (await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
                {
                    await NotifyAsync([userId, otherUserId], existing, "members", cancellationToken).ConfigureAwait(false);
                    return (await SummaryAsync(userId, existing, cancellationToken).ConfigureAwait(false), false);
                }

                if (attempt == SaveAttempts)
                {
                    throw new ConflictException("conflict");
                }

                continue;
            }

            var conversation = Conversation.CreateDirect(Guid.CreateVersion7(now), scope, organizationId, userId, otherUserId, now);
            var created = ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.Created, userId, null, null, now);
            store.Add(conversation);
            store.Add(created);
            store.Add(ConversationParticipant.Join(Guid.CreateVersion7(now), conversation.Id, userId, ParticipantRole.Member, created.Sequence, now));
            store.Add(ConversationParticipant.Join(Guid.CreateVersion7(now), conversation.Id, otherUserId, ParticipantRole.Member, created.Sequence, now));
            if (await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                await NotifyAsync([userId, otherUserId], conversation, "created", cancellationToken).ConfigureAwait(false);
                return (await SummaryAsync(userId, conversation, cancellationToken).ConfigureAwait(false), true);
            }

            if (attempt == SaveAttempts)
            {
                throw new ConflictException("conflict");
            }

            // Lost a race with the other person opening the same conversation: load theirs.
        }
    }

    public async Task<ConversationSummaryView> CreateGroupAsync(
        Guid userId, string? title, Guid? organizationId, IReadOnlyCollection<Guid>? memberIds, CancellationToken cancellationToken)
    {
        if (organizationId is { } orgId)
        {
            await RequireOrganizationMemberAsync(userId, orgId, cancellationToken).ConfigureAwait(false);
        }

        var members = (memberIds ?? []).Where(id => id != userId).Distinct().ToList();
        if (members.Count + 1 > settings.Value.MaxGroupParticipants)
        {
            throw new RequestValidationException("userIds", "too_many_participants");
        }

        await RequireAllowedAsync(userId, members, organizationId, cancellationToken).ConfigureAwait(false);

        var now = time.GetUtcNow();
        var scope = organizationId is null ? ConversationScope.Personal : ConversationScope.Organization;
        var conversation = Conversation.CreateGroup(Guid.CreateVersion7(now), scope, organizationId, title ?? string.Empty, userId, now);
        var created = ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.Created, userId, null, conversation.Title, now);
        store.Add(conversation);
        store.Add(created);
        store.Add(ConversationParticipant.Join(Guid.CreateVersion7(now), conversation.Id, userId, ParticipantRole.Owner, created.Sequence, now));
        foreach (var memberId in members)
        {
            var added = ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.MemberAdded, userId, memberId, null, now);
            store.Add(added);
            store.Add(ConversationParticipant.Join(Guid.CreateVersion7(now), conversation.Id, memberId, ParticipantRole.Member, added.Sequence, now));
        }

        if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("conflict");
        }

        await NotifyAsync([userId, .. members], conversation, "created", cancellationToken).ConfigureAwait(false);
        return await SummaryAsync(userId, conversation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a website conversation (Phase 10): the visitor plus the widget's responders who are
    /// still active members of the widget's organization. Responders are told like for any new conversation.
    /// </summary>
    public async Task<Conversation> CreateWebsiteConversationAsync(
        Guid organizationId, Guid widgetId, Guid visitorUserId, string visitorName, IReadOnlyCollection<Guid> responderIds, CancellationToken cancellationToken)
    {
        var responders = new List<Guid>();
        foreach (var id in responderIds.Distinct())
        {
            if (await organizations.FindMemberAsync(organizationId, id, cancellationToken).ConfigureAwait(false) is { IsActive: true })
            {
                responders.Add(id);
            }
        }

        if (responders.Count == 0)
        {
            throw new DependencyUnavailableException("widget_no_responders");
        }

        var now = time.GetUtcNow();
        var conversation = Conversation.CreateWebsite(Guid.CreateVersion7(now), organizationId, widgetId, visitorName, visitorUserId, now);
        var created = ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.Created, visitorUserId, null, null, now);
        store.Add(conversation);
        store.Add(created);
        store.Add(ConversationParticipant.JoinVisitor(Guid.CreateVersion7(now), conversation.Id, visitorUserId, created.Sequence, now));
        foreach (var responder in responders)
        {
            store.Add(ConversationParticipant.Join(Guid.CreateVersion7(now), conversation.Id, responder, ParticipantRole.Member, created.Sequence, now));
        }

        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        await NotifyAsync([visitorUserId, .. responders], conversation, "created", cancellationToken).ConfigureAwait(false);

        // Support staff (Phase 11) see new website conversations in their inbox.
        var staff = (await organizations.ListActiveMembersAsync(organizationId, cancellationToken).ConfigureAwait(false))
            .Where(IsSupportStaff).Select(m => m.UserId).Except(responders).ToList();
        await NotifyAsync(staff, conversation, "inbox", cancellationToken).ConfigureAwait(false);
        return conversation;
    }

    // ---- Reading ---------------------------------------------------------------------------

    /// <summary>The caller's conversations, most recent activity first. Organization conversations need active membership.</summary>
    public async Task<IReadOnlyList<ConversationSummaryView>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        (await ListPageAsync(userId, after: null, limit: int.MaxValue, cancellationToken).ConfigureAwait(false)).Items;

    public const int DefaultListPageSize = 50;
    public const int MaxListPageSize = 200;

    /// <summary>
    /// One page of the caller's conversations (Phase 22), in the same order as <see cref="ListAsync"/>.
    /// The page boundary is a position (activity time, id), so a conversation that becomes active while
    /// someone pages moves to the top and is not repeated further down; realtime updates bring it in.
    /// </summary>
    public async Task<ConversationListPage> ListPageAsync(Guid userId, ConversationListPosition? after, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);


        var memberships = (await organizations.ListActiveMembershipsAsync(userId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(m => m.Organization.Id, m => m.Organization.Name);
        var guestOf = (await organizations.ListGuestOrganizationsAsync(userId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(o => o.Id, o => o.Name);
        var workspaces = await organizations.ListActiveWorkspaceIdsAsync(userId, cancellationToken).ConfigureAwait(false);
        var participations = (await store.ListForUserAsync(userId, cancellationToken).ConfigureAwait(false))
            .Where(p => p.Conversation.OrganizationId is not { } org
                || (p.Participant.IsGuest ? guestOf.ContainsKey(org)
                    : memberships.ContainsKey(org) && (p.Conversation.WorkspaceId is not { } ws || workspaces.Contains(ws))))
            .ToList();
        foreach (var (id, name) in guestOf)
        {
            memberships.TryAdd(id, name);
        }

        var ordered = participations
            .Select(p => (Item: p, Position: new ConversationListPosition(p.Conversation.LastActivityAtUtc.UtcTicks, p.Conversation.Id)))
            .Where(x => after is not { } a || x.Position.IsAfter(a))
            .OrderByDescending(x => x.Position.ActivityTicks)
            .ThenByDescending(x => x.Position.Id)
            .ToList();
        var page = limit < ordered.Count ? ordered.Take(limit).ToList() : ordered;
        var hasMore = page.Count < ordered.Count;

        // Redesign 2026-11: the newest message each person may see (not before their visible history).
        var newest = await store.FindLastMessagesAsync(page.Select(x => x.Item.Conversation.Id).ToList(), cancellationToken).ConfigureAwait(false);
        ConversationEvent? LastFor(Participation p) =>
            newest.TryGetValue(p.Conversation.Id, out var e) && e.Sequence >= p.Participant.VisibleFromSequence ? e : null;

        // Profiles are looked up for this page only: that is most of the cost of a long list.
        var profiles = await directory.GetSummariesAsync(
            page.Where(x => x.Item.CounterpartUserId is not null).Select(x => x.Item.CounterpartUserId!.Value)
                .Concat(page.Select(x => LastFor(x.Item)?.ActorUserId).OfType<Guid>())
                .Distinct().ToList(),
            cancellationToken).ConfigureAwait(false);

        var items = page
            .Select(x => new ConversationSummaryView(
                x.Item.Conversation,
                x.Item.Participant,
                x.Item.UnreadCount,
                x.Item.CounterpartUserId is { } other ? profiles.GetValueOrDefault(other) : null,
                x.Item.Conversation.OrganizationId is { } org ? memberships[org] : null,
                LastFor(x.Item) is { } last ? new LastMessageView(last, profiles.GetValueOrDefault(last.ActorUserId)) : null))
            .ToList();
        return new ConversationListPage(items, hasMore ? page[^1].Position : null);
    }

    public async Task<ConversationDetailView> GetAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var (conversation, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var participants = await store.ListActiveParticipantsAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var users = await directory.GetSummariesAsync(participants.Select(p => p.UserId).ToList(), cancellationToken).ConfigureAwait(false);
        var views = participants
            .Where(p => users.ContainsKey(p.UserId))
            .Select(p => new ParticipantView(p, users[p.UserId]))
            .OrderByDescending(p => p.Participant.Role)
            .ThenBy(p => p.User.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new ConversationDetailView(await SummaryAsync(userId, conversation, me, cancellationToken).ConfigureAwait(false), views);
    }

    /// <summary>
    /// History visible to the caller, newest page first. Pass <paramref name="beforeSequence"/> from
    /// the previous page to continue; items within a page are in ascending order.
    /// </summary>
    public async Task<HistoryPage> GetHistoryAsync(Guid userId, Guid conversationId, long? beforeSequence, int? limit, CancellationToken cancellationToken)
    {
        var size = limit ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
        {
            throw new RequestValidationException("limit", "limit_out_of_range");
        }

        var (_, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var page = await store.ListHistoryAsync(conversationId, me.VisibleFromSequence, beforeSequence ?? long.MaxValue, size + 1, cancellationToken)
            .ConfigureAwait(false);
        var hasMore = page.Count > size;
        var items = page.Take(size).OrderBy(e => e.Sequence).ToList();
        var views = await WithCallerStateAsync(userId, await DescribeAsync(items, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        return new HistoryPage(views, hasMore ? items[0].Sequence : null, hasMore);
    }

    /// <summary>People and reply previews for history entries (used by history and message events).</summary>
    public Task<IReadOnlyList<HistoryEntryView>> DescribeAsync(IReadOnlyList<ConversationEvent> entries, CancellationToken cancellationToken) =>
        DescribeAsync(entries, NewEntry.None, cancellationToken);

    /// <summary>
    /// Describes a message saved a moment ago (Phase 23): it cannot have reactions, pins or thread
    /// replies yet, and it has files or mentions only when the send added them, so those lookups are
    /// skipped. Sending a message reads far less than reading it back from history.
    /// </summary>
    public async Task<HistoryEntryView> DescribeNewAsync(ConversationEvent entry, bool hasFiles, bool hasMentions, CancellationToken cancellationToken) =>
        (await DescribeAsync([entry], new NewEntry(true, hasFiles, hasMentions), cancellationToken).ConfigureAwait(false))[0];

    private readonly record struct NewEntry(bool IsNew, bool HasFiles, bool HasMentions)
    {
        public static NewEntry None => default;
    }

    private async Task<IReadOnlyList<HistoryEntryView>> DescribeAsync(IReadOnlyList<ConversationEvent> entries, NewEntry fresh, CancellationToken cancellationToken)
    {
        var replyIds = entries.Where(e => e.ReplyToEntryId is not null).Select(e => e.ReplyToEntryId!.Value).Distinct().ToList();
        var targets = replyIds.Count == 0
            ? new Dictionary<Guid, ConversationEvent>()
            : (await store.FindEntriesAsync(replyIds, cancellationToken).ConfigureAwait(false)).ToDictionary(e => e.Id);
        var userIds = entries.Select(e => e.ActorUserId)
            .Concat(entries.Where(e => e.SubjectUserId is not null).Select(e => e.SubjectUserId!.Value))
            .Concat(targets.Values.Select(t => t.ActorUserId))
            .Concat(entries.Where(e => e.ForwardedFromUserId is not null).Select(e => e.ForwardedFromUserId!.Value))
            .Distinct().ToList();
        var users = await directory.GetSummariesAsync(userIds, cancellationToken).ConfigureAwait(false);
        var messageIds = entries.Where(e => e is { IsMessage: true, IsDeleted: false }).Select(e => e.Id).ToList();
        var files = messageIds.Count == 0 || (fresh.IsNew && !fresh.HasFiles)
            ? []
            : (await attachments.ListReadyForMessagesAsync(messageIds, cancellationToken).ConfigureAwait(false))
                .GroupBy(a => a.MessageEntryId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id).Select(Attachments.AttachmentService.ToView).ToList());
        var reactions = messageIds.Count == 0 || fresh.IsNew
            ? []
            : (await store.ListReactionsAsync(messageIds, cancellationToken).ConfigureAwait(false))
                .GroupBy(r => r.EntryId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<ReactionView>)g
                    .GroupBy(r => r.Emoji, StringComparer.Ordinal)
                    .OrderBy(e => e.Min(r => r.CreatedAtUtc))
                    .Select(e => new ReactionView(e.Key, e.OrderBy(r => r.CreatedAtUtc).Select(r => r.UserId).ToList()))
                    .ToList());
        var mentions = messageIds.Count == 0 || (fresh.IsNew && !fresh.HasMentions)
            ? new Dictionary<Guid, MentionsView>()
            : await store.DescribeMentionsAsync(messageIds, cancellationToken).ConfigureAwait(false);
        var pinned = messageIds.Count == 0 || fresh.IsNew ? [] : (await store.ListPinnedEntryIdsAsync(messageIds, cancellationToken).ConfigureAwait(false)).ToHashSet();
        return entries.Select(e =>
        {
            ReplyPreview? reply = null;
            if (e.ReplyToEntryId is { } id && targets.TryGetValue(id, out var t))
            {
                var excerpt = t.Body is null ? null : MessageText.Excerpt(t.Body) ?? string.Empty;
                reply = new ReplyPreview(t.Id, t.Sequence, users.GetValueOrDefault(t.ActorUserId), excerpt, t.IsDeleted);
            }

            return new HistoryEntryView(e, users.GetValueOrDefault(e.ActorUserId), e.SubjectUserId is { } s ? users.GetValueOrDefault(s) : null, reply,
                files.TryGetValue(e.Id, out var list) ? list : null,
                reactions.GetValueOrDefault(e.Id),
                e.ThreadReplyCount > 0 ? new ThreadView(e.ThreadReplyCount, e.ThreadLastReplyAtUtc) : null,
                mentions.GetValueOrDefault(e.Id),
                pinned.Contains(e.Id),
                e.ForwardedFromUserId is { } f ? users.GetValueOrDefault(f) : null);
        }).ToList();
    }

    /// <summary>
    /// Adds the caller's own state: thread following and unread replies (Phase 16) and whether they
    /// saved each message (Phase 18).
    /// </summary>
    public async Task<IReadOnlyList<HistoryEntryView>> WithCallerStateAsync(Guid userId, IReadOnlyList<HistoryEntryView> views, CancellationToken cancellationToken)
    {
        var messageIds = views.Where(v => v.Entry is { IsMessage: true, IsDeleted: false }).Select(v => v.Entry.Id).ToList();
        var saved = messageIds.Count == 0 ? [] : (await store.ListSavedEntryIdsAsync(userId, messageIds, cancellationToken).ConfigureAwait(false)).ToHashSet();
        var roots = views.Where(v => v.Thread is not null).Select(v => v.Entry.Id).ToList();
        var follows = roots.Count == 0 ? [] : await store.ListThreadFollowsAsync(userId, roots, cancellationToken).ConfigureAwait(false);
        var unread = await store.CountThreadUnreadAsync(userId, follows.Where(f => f.Following).ToList(), cancellationToken).ConfigureAwait(false);
        var byRoot = follows.ToDictionary(f => f.RootId);
        return views.Select(v => v with
        {
            Saved = v.Entry is { IsMessage: true, IsDeleted: false } ? saved.Contains(v.Entry.Id) : null,
            Thread = v.Thread is null ? null : v.Thread with
            {
                Following = byRoot.TryGetValue(v.Entry.Id, out var f) && f.Following,
                Unread = unread.GetValueOrDefault(v.Entry.Id),
            },
        }).ToList();
    }

    // ---- Participant state -----------------------------------------------------------------

    /// <summary>Moves the caller's read position forward (never backwards) and returns the remaining unread count.</summary>
    public async Task<int> MarkReadAsync(Guid userId, Guid conversationId, long sequence, CancellationToken cancellationToken)
    {
        var (conversation, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        if (me.MarkRead(sequence, conversation.LastSequence))
        {
            await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
            await publisher.PublishToUsersAsync([userId], new ConversationUpdated(conversationId, "read", conversation.LastSequence), cancellationToken)
                .ConfigureAwait(false);

            // Read receipts for the other participants (ADR-023).
            var recipients = await store.ListRecipientsAsync(conversationId, long.MaxValue, cancellationToken).ConfigureAwait(false);
            await publisher.PublishToUsersAsync(recipients, new Messaging.ReceiptChanged(conversationId, userId, "read", me.LastReadSequence), cancellationToken)
                .ConfigureAwait(false);
        }

        return await store.CountUnreadAsync(conversationId, userId, me.LastReadSequence, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The caller's notification level for the conversation (Phase 17).</summary>
    public async Task<ConversationSummaryView> SetNotificationLevelAsync(Guid userId, Guid conversationId, NotificationLevel level, CancellationToken cancellationToken)
    {
        var (conversation, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        me.SetNotificationLevel(level);
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        return await SummaryAsync(userId, conversation, me, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Files shared in the conversation that the caller may see (redesign 2026-11), newest first, a
    /// page at a time by message position. A page boundary inside one message's files never splits it:
    /// the next page starts before that message.
    /// </summary>
    public async Task<SharedFilesPage> ListSharedFilesAsync(Guid userId, Guid conversationId, long? beforeSequence, int? limit, CancellationToken cancellationToken)
    {
        var (_, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var size = Math.Clamp(limit ?? 30, 1, 100);
        var rows = await store.ListSharedFilesAsync(conversationId, me.VisibleFromSequence, beforeSequence, size + 1, cancellationToken).ConfigureAwait(false);
        var hasMore = rows.Count > size;
        var page = hasMore ? rows.Take(size).ToList() : rows.ToList();
        if (hasMore)
        {
            // Keep every file of the last message together on the next page.
            var cut = page[^1].Entry.Sequence;
            if (rows[size].Entry.Sequence == cut && page.Any(r => r.Entry.Sequence != cut))
            {
                page = page.Where(r => r.Entry.Sequence != cut).ToList();
            }
        }

        var authors = await directory.GetSummariesAsync(page.Select(r => r.Entry.ActorUserId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        var items = page.Select(r => new SharedFileView(Attachments.AttachmentService.ToView(r.Attachment), r.Entry, authors.GetValueOrDefault(r.Entry.ActorUserId))).ToList();
        var next = hasMore && items.Count > 0 ? items.Min(i => i.Entry.Sequence) : (long?)null;
        return new SharedFilesPage(items, next);
    }

    /// <summary>Pins or unpins the conversation in the caller's own list (redesign 2026-11).</summary>
    public async Task<ConversationSummaryView> SetPinnedAsync(Guid userId, Guid conversationId, bool pinned, CancellationToken cancellationToken)
    {
        var (conversation, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        me.SetPinned(pinned, time.GetUtcNow());
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        return await SummaryAsync(userId, conversation, me, cancellationToken).ConfigureAwait(false);
    }

    // ---- Group management ------------------------------------------------------------------

    public async Task<ConversationDetailView> UpdateAsync(
        Guid userId, Guid conversationId, string? title, string? description, CancellationToken cancellationToken)
    {
        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var (conversation, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
            conversation.RequireGroup();
            RequirePermission(me, ConversationPermission.ManageSettings);
            var now = time.GetUtcNow();
            if (title is not null && title.Trim() != conversation.Title)
            {
                conversation.Rename(title);
                store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.Renamed, userId, null, conversation.Title, now));
            }

            if (description is not null && (description.Trim().Length == 0 ? null : description.Trim()) != conversation.Description)
            {
                conversation.Describe(description);
                store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.DescriptionChanged, userId, null, conversation.Description, now));
            }

            updated = conversation;
        }, cancellationToken).ConfigureAwait(false);

        await NotifyParticipantsAsync(updated!, "updated", [], cancellationToken).ConfigureAwait(false);
        return await GetAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds people (or brings back people who left); their history starts at the entry recording the addition.</summary>
    public async Task<ConversationDetailView> AddParticipantsAsync(
        Guid userId, Guid conversationId, IReadOnlyCollection<Guid>? userIds, CancellationToken cancellationToken)
    {
        var toAdd = (userIds ?? []).Distinct().ToList();
        if (toAdd.Count == 0)
        {
            throw new RequestValidationException("userIds", "required");
        }

        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var (conversation, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
            conversation.RequireGroup();
            RequirePermission(me, ConversationPermission.ManageMembers);
            var active = await store.ListActiveParticipantsAsync(conversationId, cancellationToken).ConfigureAwait(false);
            if (toAdd.Any(id => active.Any(p => p.UserId == id)))
            {
                throw new RequestValidationException("userIds", "already_participant");
            }

            if (active.Count + toAdd.Count > settings.Value.MaxGroupParticipants)
            {
                throw new RequestValidationException("userIds", "too_many_participants");
            }

            await RequireAllowedAsync(userId, toAdd, conversation.OrganizationId, cancellationToken).ConfigureAwait(false);
            var now = time.GetUtcNow();
            foreach (var memberId in toAdd)
            {
                var added = ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.MemberAdded, userId, memberId, null, now);
                store.Add(added);
                if (await store.FindParticipantAsync(conversationId, memberId, cancellationToken).ConfigureAwait(false) is { } previous)
                {
                    previous.Rejoin(ParticipantRole.Member, added.Sequence, now);
                }
                else
                {
                    store.Add(ConversationParticipant.Join(Guid.CreateVersion7(now), conversationId, memberId, ParticipantRole.Member, added.Sequence, now));
                }
            }

            updated = conversation;
        }, cancellationToken).ConfigureAwait(false);

        await NotifyParticipantsAsync(updated!, "members", [], cancellationToken).ConfigureAwait(false);
        return await GetAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a participant. Administrators may remove members; owners may remove anyone.
    /// Removing yourself is leaving. The removed person loses access immediately, including open
    /// realtime connections.
    /// </summary>
    public async Task RemoveParticipantAsync(Guid userId, Guid conversationId, Guid targetUserId, CancellationToken cancellationToken)
    {
        if (targetUserId == userId)
        {
            await LeaveAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
            return;
        }

        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var (conversation, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
            conversation.RequireGroup();
            RequirePermission(me, ConversationPermission.ManageMembers);
            var target = await RequireActiveParticipantAsync(conversationId, targetUserId, cancellationToken).ConfigureAwait(false);
            if (target.Role != ParticipantRole.Member)
            {
                RequirePermission(me, ConversationPermission.ManageRoles);
            }

            var now = time.GetUtcNow();
            target.Remove(now);
            store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.MemberRemoved, userId, targetUserId, null, now));
            updated = conversation;
        }, cancellationToken).ConfigureAwait(false);

        await EndAccessAsync(updated!, targetUserId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Leaves a group. If the last owner leaves, the longest-standing remaining participant becomes owner.</summary>
    public async Task LeaveAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var (conversation, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
            conversation.RequireGroup();
            var now = time.GetUtcNow();
            var wasOwner = me.Role == ParticipantRole.Owner;
            me.Leave(now);
            store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.MemberLeft, userId, null, null, now));

            var remaining = (await store.ListActiveParticipantsAsync(conversationId, cancellationToken).ConfigureAwait(false))
                .Where(p => p.UserId != userId)
                .ToList();
            if (wasOwner && remaining.Count > 0 && remaining.All(p => p.Role != ParticipantRole.Owner))
            {
                var successor = remaining.OrderBy(p => p.JoinedAtUtc).ThenBy(p => p.Id).First();
                successor.ChangeRole(ParticipantRole.Owner);
                store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.RoleChanged, userId, successor.UserId, "owner", now));
            }

            updated = conversation;
        }, cancellationToken).ConfigureAwait(false);

        await EndAccessAsync(updated!, userId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Owners change roles; the last owner cannot be demoted (they may leave instead).</summary>
    public async Task<ConversationDetailView> ChangeRoleAsync(
        Guid userId, Guid conversationId, Guid targetUserId, ParticipantRole role, CancellationToken cancellationToken)
    {
        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var (conversation, me) = await RequireAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
            conversation.RequireGroup();
            RequirePermission(me, ConversationPermission.ManageRoles);
            var target = await RequireActiveParticipantAsync(conversationId, targetUserId, cancellationToken).ConfigureAwait(false);
            if (target.Role == role)
            {
                updated = conversation;
                return;
            }

            if (target.Role == ParticipantRole.Owner
                && !(await store.ListActiveParticipantsAsync(conversationId, cancellationToken).ConfigureAwait(false))
                    .Any(p => p.Role == ParticipantRole.Owner && p.UserId != targetUserId))
            {
                throw new ConflictException("last_owner");
            }

            var now = time.GetUtcNow();
            target.ChangeRole(role);
            store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.RoleChanged, userId, targetUserId,
                role.ToString().ToLowerInvariant(), now));
            updated = conversation;
        }, cancellationToken).ConfigureAwait(false);

        await NotifyParticipantsAsync(updated!, "members", [], cancellationToken).ConfigureAwait(false);
        return await GetAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ends the user's participation in every conversation of an organization they no longer belong
    /// to, so rejoining the organization later does not restore old conversations (ADR-023).
    /// </summary>
    public async Task RemoveFromOrganizationAsync(Guid actorUserId, Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        foreach (var conversationId in await store.ListActiveConversationIdsInOrganizationAsync(organizationId, userId, cancellationToken).ConfigureAwait(false))
        {
            Conversation? updated = null;
            await MutateAsync(async () =>
            {
                var conversation = await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false);
                var participant = await store.FindParticipantAsync(conversationId, userId, cancellationToken).ConfigureAwait(false);
                if (conversation is null || participant is not { IsActive: true })
                {
                    return;
                }

                var now = time.GetUtcNow();
                participant.Remove(now);
                var kind = actorUserId == userId ? ConversationEventKind.MemberLeft : ConversationEventKind.MemberRemoved;
                store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), conversation, kind, actorUserId,
                    kind == ConversationEventKind.MemberRemoved ? userId : null, null, now));
                updated = conversation;
            }, cancellationToken).ConfigureAwait(false);

            if (updated is not null)
            {
                await EndAccessAsync(updated, userId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // ---- Helpers ---------------------------------------------------------------------------

    /// <summary>Active participation plus, for organization conversations, active organization membership.</summary>
    public async Task<(Conversation Conversation, ConversationParticipant Me)> RequireAccessAsync(
        Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var participant = await store.FindParticipantAsync(conversationId, userId, cancellationToken).ConfigureAwait(false);
        var conversation = participant is { IsActive: true } ? await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false) : null;
        if (conversation is null || !await MayUseAsync(conversation, participant!, cancellationToken).ConfigureAwait(false))
        {
            throw new ResourceNotFoundException(NotFound);
        }

        return (conversation, participant!);
    }

    /// <summary>
    /// The organization side of access for an active participant: website visitors (Phase 10) need
    /// nothing more; guests (Phase 11) an active guest record in an organization that allows guests;
    /// everyone else active membership, plus active workspace membership for channels.
    /// </summary>
    private async Task<bool> MayUseAsync(Conversation conversation, ConversationParticipant participant, CancellationToken cancellationToken)
    {
        if (conversation.OrganizationId is not { } org || participant.IsVisitor)
        {
            return true;
        }

        if (participant.IsGuest)
        {
            return await organizations.FindGuestAsync(org, participant.UserId, cancellationToken).ConfigureAwait(false) is { IsActive: true }
                && await organizations.FindOrganizationAsync(org, cancellationToken).ConfigureAwait(false) is { GuestsEnabled: true };
        }

        return await organizations.FindMemberAsync(org, participant.UserId, cancellationToken).ConfigureAwait(false) is { IsActive: true }
            && (conversation.WorkspaceId is not { } workspace
                || await organizations.FindWorkspaceMemberAsync(workspace, participant.UserId, cancellationToken).ConfigureAwait(false) is { IsActive: true });
    }

    /// <summary>Runs a mutation that takes sequence numbers, retrying when a concurrent writer took them first.</summary>
    private async Task MutateAsync(Func<Task> mutation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await mutation().ConfigureAwait(false);
            if (await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (attempt == SaveAttempts)
            {
                throw new ConflictException("conflict");
            }
        }
    }

    private async Task SaveOrConflictAsync(CancellationToken cancellationToken)
    {
        if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("conflict");
        }
    }

    private async Task RequireOrganizationMemberAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        if (await organizations.FindMemberAsync(organizationId, userId, cancellationToken).ConfigureAwait(false) is not { IsActive: true })
        {
            throw new ResourceNotFoundException("organization_not_found");
        }
    }

    /// <summary>
    /// Whether <paramref name="userId"/> may put <paramref name="otherUserId"/> into a conversation
    /// of the scope: never across a block; personal scope needs a contact, organization scope an
    /// active member.
    /// </summary>
    private async Task<bool> MayIncludeAsync(Guid userId, Guid otherUserId, Guid? organizationId, CancellationToken cancellationToken)
    {
        if ((await contacts.ListBlockPartnersAsync(userId, cancellationToken).ConfigureAwait(false)).Contains(otherUserId))
        {
            return false;
        }

        return organizationId is { } org
            ? await organizations.FindMemberAsync(org, otherUserId, cancellationToken).ConfigureAwait(false) is { IsActive: true }
            : await contacts.FindContactAsync(UserPair.Of(userId, otherUserId), cancellationToken).ConfigureAwait(false) is not null;
    }

    private async Task RequireAllowedAsync(Guid userId, IReadOnlyCollection<Guid> others, Guid? organizationId, CancellationToken cancellationToken)
    {
        foreach (var other in others)
        {
            // One code for every reason, so blocks are not revealed.
            if (other == userId || !await MayIncludeAsync(userId, other, organizationId, cancellationToken).ConfigureAwait(false))
            {
                throw new RequestValidationException("userIds", "participant_not_allowed");
            }
        }
    }

    private async Task<ConversationParticipant> RequireActiveParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken) =>
        await store.FindParticipantAsync(conversationId, userId, cancellationToken).ConfigureAwait(false) is { IsActive: true } participant
            ? participant
            : throw new ResourceNotFoundException("participant_not_found");

    private static void RequirePermission(ConversationParticipant participant, ConversationPermission permission)
    {
        if (!participant.Can(permission))
        {
            throw new ForbiddenException();
        }
    }

    private async Task<ConversationSummaryView> SummaryAsync(Guid userId, Conversation conversation, CancellationToken cancellationToken)
    {
        var me = await store.FindParticipantAsync(conversation.Id, userId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException(NotFound);
        return await SummaryAsync(userId, conversation, me, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConversationSummaryView> SummaryAsync(
        Guid userId, Conversation conversation, ConversationParticipant me, CancellationToken cancellationToken)
    {
        var unread = await store.CountUnreadAsync(conversation.Id, userId, me.LastReadSequence, cancellationToken).ConfigureAwait(false);
        UserSummary? counterpart = null;
        if (conversation.Type == ConversationType.Direct)
        {
            var other = (await store.ListActiveParticipantsAsync(conversation.Id, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(p => p.UserId != userId);
            if (other is not null)
            {
                counterpart = (await directory.GetSummariesAsync([other.UserId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(other.UserId);
            }
        }

        string? organizationName = null;
        if (conversation.OrganizationId is { } org)
        {
            organizationName = (await organizations.FindOrganizationAsync(org, cancellationToken).ConfigureAwait(false))?.Name;
        }

        return new ConversationSummaryView(conversation, me, unread, counterpart, organizationName);
    }

    private async Task NotifyParticipantsAsync(Conversation conversation, string change, IReadOnlyCollection<Guid> extra, CancellationToken cancellationToken)
    {
        var participants = await store.ListActiveParticipantsAsync(conversation.Id, cancellationToken).ConfigureAwait(false);
        await NotifyAsync([.. participants.Select(p => p.UserId), .. extra], conversation, change, cancellationToken).ConfigureAwait(false);
    }

    private Task NotifyAsync(IEnumerable<Guid> userIds, Conversation conversation, string change, CancellationToken cancellationToken) =>
        publisher.PublishToUsersAsync(userIds.Distinct().ToList(), new ConversationUpdated(conversation.Id, change, conversation.LastSequence), cancellationToken);

    /// <summary>After leaving or removal: close realtime access, tell the person, update the others.</summary>
    private async Task EndAccessAsync(Conversation conversation, Guid userId, CancellationToken cancellationToken)
    {
        await realtime.RevokeConversationAccessAsync(userId, conversation.Id, cancellationToken).ConfigureAwait(false);
        await NotifyAsync([userId], conversation, "removed", cancellationToken).ConfigureAwait(false);
        await NotifyParticipantsAsync(conversation, "members", [], cancellationToken).ConfigureAwait(false);
    }
}
