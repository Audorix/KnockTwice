using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Users;
using Audorix.KnockTwice.Domain.Conversations;
using Audorix.KnockTwice.Domain.Organizations;

namespace Audorix.KnockTwice.Application.Conversations;

/// <summary>One website conversation in the support inbox.</summary>
public sealed record SupportItemView(Conversation Conversation, bool Participating, int UnreadCount, UserSummary? Assignee);

/// <param name="Status">Null for every status.</param>
/// <param name="Search">Matched against the customer's name and the subject.</param>
/// <param name="OldestFirst">Longest-waiting first (least recent activity); otherwise most recent first.</param>
/// <param name="Assigned">True: assigned to someone; false: unassigned; null: either.</param>
public sealed record SupportInboxQuery(SupportStatus? Status, bool AssignedToMe, string? Search, bool OldestFirst, int Skip, int Take, bool? Assigned = null);

/// <summary>Conversations per status under the other filters (assignment, search).</summary>
public sealed record SupportInboxCounts(int Open, int Pending, int Closed, int All, int UnassignedOpen = 0);

/// <param name="LastMessage">The newest message, for the preview; null when there is none.</param>
/// <param name="FirstMessage">The first message, shown as the title while there is no subject.</param>
public sealed record SupportInboxItem(SupportItemView Item, ConversationEvent? LastMessage, ConversationEvent? FirstMessage);

public sealed record SupportInboxPage(IReadOnlyList<SupportInboxItem> Items, int Total, SupportInboxCounts Counts);

public sealed record SupportNoteView(SupportNote Note, UserSummary? Author);

/// <summary>
/// Support-agent foundations (Phase 11, owner decision "agent role + assignment"). Website
/// conversations have a status (open/closed) and at most one assigned agent. Support agents and
/// organization owners/administrators see the whole inbox and assign anyone eligible; responders who
/// take part in a conversation see and work that conversation. Someone who takes a conversation sees
/// its whole history (unlike groups): support needs the visitor's earlier messages.
/// </summary>
public sealed partial class ConversationService : ISupportRoutingNotifier
{
    public const int InboxLimit = 200;

    public async Task<IReadOnlyList<SupportItemView>> ListInboxAsync(
        Guid userId, Guid organizationId, SupportStatus? status, bool assignedToMe, CancellationToken cancellationToken)
    {
        var membership = await organizations.FindMemberAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);
        if (membership is not { IsActive: true })
        {
            throw new ResourceNotFoundException("organization_not_found");
        }

        var access = await GetSupportAccessAsync(userId, organizationId, membership, cancellationToken).ConfigureAwait(false);
        if (!access.CanEnter) throw new ForbiddenException();
        var fullInbox = access.CanReadAll;
        var items = new List<(Conversation Conversation, bool Participating, int Unread)>();
        foreach (var conversation in await store.ListWebsiteConversationsAsync(organizationId, status, InboxLimit, cancellationToken).ConfigureAwait(false))
        {
            if (assignedToMe && conversation.AssignedToUserId != userId)
            {
                continue;
            }

            var me = await store.FindParticipantAsync(conversation.Id, userId, cancellationToken).ConfigureAwait(false);
            var participating = me is { IsActive: true };
            if (!fullInbox && (!access.CanReadParticipating || !participating))
            {
                continue;
            }

            var unread = participating ? await store.CountUnreadAsync(conversation.Id, userId, me!.LastReadSequence, cancellationToken).ConfigureAwait(false) : 0;
            items.Add((conversation, participating, unread));
        }

        var assignees = await directory.GetSummariesAsync(
            items.Where(i => i.Conversation.AssignedToUserId is not null).Select(i => i.Conversation.AssignedToUserId!.Value).Distinct().ToList(),
            cancellationToken).ConfigureAwait(false);
        return items.Select(i => new SupportItemView(i.Conversation, i.Participating, i.Unread,
            i.Conversation.AssignedToUserId is { } a ? assignees.GetValueOrDefault(a) : null)).ToList();
    }

    public const int MaxInboxPageSize = 100;

    /// <summary>
    /// One page of the support inbox (redesign 2026-11): filtered by status, assignment and text,
    /// ordered by activity, with totals per status and each conversation's newest message.
    /// </summary>
    public async Task<SupportInboxPage> ListInboxPageAsync(Guid userId, Guid organizationId, SupportInboxQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Take is < 1 or > MaxInboxPageSize)
        {
            throw new RequestValidationException("take", "limit_out_of_range");
        }

        var membership = await organizations.FindMemberAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);
        if (membership is not { IsActive: true })
        {
            throw new ResourceNotFoundException("organization_not_found");
        }

        var access = await GetSupportAccessAsync(userId, organizationId, membership, cancellationToken).ConfigureAwait(false);
        if (!access.CanEnter) throw new ForbiddenException();
        var fullInbox = access.CanReadAll;
        var all = await store.ListWebsiteConversationsAsync(organizationId, null, int.MaxValue, cancellationToken).ConfigureAwait(false);
        var mine = await store.ListParticipationsAsync(userId, all.Select(c => c.Id).ToList(), cancellationToken).ConfigureAwait(false);
        var text = query.Search?.Trim();
        var visible = all
            .Where(c => fullInbox || (access.CanReadParticipating && mine.GetValueOrDefault(c.Id) is { IsActive: true }))
            .Where(c => string.IsNullOrEmpty(text)
                || (c.Title?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                || (c.Subject?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
        var unassignedOpen = visible.Count(c => c.SupportStatus == SupportStatus.Open && c.AssignedToUserId is null);
        var matching = visible
            .Where(c => !query.AssignedToMe || c.AssignedToUserId == userId)
            .Where(c => query.Assigned is null || (c.AssignedToUserId is not null) == query.Assigned)
            .ToList();
        var counts = new SupportInboxCounts(
            matching.Count(c => c.SupportStatus == SupportStatus.Open),
            matching.Count(c => c.SupportStatus == SupportStatus.Pending),
            matching.Count(c => c.SupportStatus == SupportStatus.Closed),
            matching.Count,
            unassignedOpen);

        var filtered = matching.Where(c => query.Status is null || c.SupportStatus == query.Status).ToList();
        var ordered = query.OldestFirst
            ? filtered.OrderBy(c => c.LastActivityAtUtc).ThenBy(c => c.Id)
            : filtered.OrderByDescending(c => c.LastActivityAtUtc).ThenByDescending(c => c.Id);
        var page = ordered.Skip(Math.Max(0, query.Skip)).Take(query.Take).ToList();

        var ids = page.Select(c => c.Id).ToList();
        var last = await store.ListEdgeMessagesAsync(ids, newest: true, cancellationToken).ConfigureAwait(false);
        var untitled = page.Where(c => c.Subject is null).Select(c => c.Id).ToList();
        var first = await store.ListEdgeMessagesAsync(untitled, newest: false, cancellationToken).ConfigureAwait(false);
        var assignees = await directory.GetSummariesAsync(
            page.Where(c => c.AssignedToUserId is not null).Select(c => c.AssignedToUserId!.Value).Distinct().ToList(), cancellationToken).ConfigureAwait(false);

        var items = new List<SupportInboxItem>();
        foreach (var conversation in page)
        {
            var me = mine.GetValueOrDefault(conversation.Id);
            var participating = me is { IsActive: true };
            var unread = participating ? await store.CountUnreadAsync(conversation.Id, userId, me!.LastReadSequence, cancellationToken).ConfigureAwait(false) : 0;
            var assignee = conversation.AssignedToUserId is { } a ? assignees.GetValueOrDefault(a) : null;
            items.Add(new SupportInboxItem(new SupportItemView(conversation, participating, unread, assignee),
                last.GetValueOrDefault(conversation.Id), first.GetValueOrDefault(conversation.Id)));
        }

        return new SupportInboxPage(items, filtered.Count, counts);
    }

    /// <summary>
    /// The whole history of a support conversation for support staff, whether or not they take part
    /// (redesign 2026-11: an agent reads a conversation before taking it). Newest page first.
    /// </summary>
    public async Task<HistoryPage> GetSupportHistoryAsync(Guid userId, Guid conversationId, long? beforeSequence, int? limit, CancellationToken cancellationToken)
    {
        var size = limit ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
        {
            throw new RequestValidationException("limit", "limit_out_of_range");
        }

        await RequireSupportConversationAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var page = await store.ListHistoryAsync(conversationId, 1, beforeSequence ?? long.MaxValue, size + 1, cancellationToken).ConfigureAwait(false);
        var hasMore = page.Count > size;
        var items = page.Take(size).OrderBy(e => e.Sequence).ToList();
        var views = await WithCallerStateAsync(userId, await DescribeAsync(items, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        return new HistoryPage(views, hasMore ? items[0].Sequence : null, hasMore);
    }

    /// <summary>Support staff join a conversation (whole history visible) to reply, without changing who it is assigned to.</summary>
    public async Task<SupportItemView> JoinSupportAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var (conversation, _) = await RequireSupportConversationAsync(userId, conversationId, cancellationToken, requireManage: true).ConfigureAwait(false);
        if (await store.FindParticipantAsync(conversationId, userId, cancellationToken).ConfigureAwait(false) is { IsActive: true })
        {
            return await SupportItemAsync(userId, conversation, cancellationToken).ConfigureAwait(false);
        }

        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var tracked = await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException(NotFound);
            var now = time.GetUtcNow();
            var existing = await store.FindParticipantAsync(conversationId, userId, cancellationToken).ConfigureAwait(false);
            if (existing is not { IsActive: true })
            {
                store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), tracked, ConversationEventKind.MemberAdded, userId, userId, null, now));
                if (existing is not null)
                {
                    existing.Rejoin(ParticipantRole.Member, 1, now);
                }
                else
                {
                    store.Add(ConversationParticipant.Join(Guid.CreateVersion7(now), conversationId, userId, ParticipantRole.Member, 1, now));
                }
            }

            updated = tracked;
        }, cancellationToken).ConfigureAwait(false);

        await NotifySupportAsync(updated!, cancellationToken).ConfigureAwait(false);
        return await SupportItemAsync(userId, updated!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets or clears what the conversation is about. Internal: the customer does not see it.</summary>
    public async Task<SupportItemView> SetSupportSubjectAsync(Guid userId, Guid conversationId, string? subject, CancellationToken cancellationToken)
    {
        await RequireSupportConversationAsync(userId, conversationId, cancellationToken, requireManage: true).ConfigureAwait(false);
        Conversation? updated = null;
        var changed = false;
        await MutateAsync(async () =>
        {
            var tracked = await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException(NotFound);
            changed = tracked.SetSubject(subject);
            updated = tracked;
        }, cancellationToken).ConfigureAwait(false);

        if (changed)
        {
            await NotifyStaffAsync(updated!, cancellationToken).ConfigureAwait(false);
        }

        return await SupportItemAsync(userId, updated!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Internal notes of a support conversation, oldest first (support staff and responders who take part).</summary>
    public async Task<IReadOnlyList<SupportNoteView>> ListSupportNotesAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        await RequireSupportConversationAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var notes = await store.ListSupportNotesAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var authors = await directory.GetSummariesAsync(notes.Select(n => n.AuthorUserId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        return notes.Select(n => new SupportNoteView(n, authors.GetValueOrDefault(n.AuthorUserId))).ToList();
    }

    /// <summary>Adds an internal note. Only staff are told about it; the customer never is.</summary>
    public async Task<SupportNoteView> AddSupportNoteAsync(Guid userId, Guid conversationId, string? body, CancellationToken cancellationToken)
    {
        var (conversation, _) = await RequireSupportConversationAsync(userId, conversationId, cancellationToken, requireManage: true).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var note = SupportNote.Create(Guid.CreateVersion7(now), conversation, userId, body, now);
        store.Add(note);
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        await NotifyStaffAsync(conversation, cancellationToken).ConfigureAwait(false);
        var author = (await directory.GetSummariesAsync([userId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(userId);
        return new SupportNoteView(note, author);
    }

    /// <summary>Support staff and the team members taking part reload; the customer is not told.</summary>
    private async Task NotifyStaffAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        var team = (await store.ListActiveParticipantsAsync(conversation.Id, cancellationToken).ConfigureAwait(false))
            .Where(p => !p.IsVisitor).Select(p => p.UserId);
        var staff = conversation.OrganizationId is { } organizationId
            ? await ListSupportStaffIdsAsync(organizationId, cancellationToken).ConfigureAwait(false)
            : [];
        await NotifyAsync(team.Concat(staff).Distinct().ToList(), conversation, "inbox", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Takes a conversation: assigns it to the caller, who joins it if needed.</summary>
    public Task<SupportItemView> ClaimAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        AssignAsync(userId, conversationId, userId, cancellationToken);

    /// <summary>
    /// Assigns (or, with null, unassigns) a website conversation. Support staff assign anyone who is
    /// support staff or already takes part; responders may only take it themselves.
    /// </summary>
    public async Task<SupportItemView> AssignAsync(Guid userId, Guid conversationId, Guid? assigneeId, CancellationToken cancellationToken)
    {
        var (conversation, fullInbox) = await RequireSupportConversationAsync(userId, conversationId, cancellationToken, requireManage: true).ConfigureAwait(false);
        var organizationId = conversation.OrganizationId!.Value;
        if (assigneeId is { } target)
        {
            if (target != userId && !fullInbox)
            {
                throw new ForbiddenException();
            }

            var member = await organizations.FindMemberAsync(organizationId, target, cancellationToken).ConfigureAwait(false);
            var takesPart = await store.FindParticipantAsync(conversationId, target, cancellationToken).ConfigureAwait(false) is { IsActive: true };
            if (member is not { IsActive: true } || (!await IsSupportAgentAsync(organizationId, target, member, cancellationToken).ConfigureAwait(false) && !takesPart))
            {
                throw new RequestValidationException("assigneeId", "assignee_not_agent");
            }
        }
        else if (!fullInbox && conversation.AssignedToUserId != userId)
        {
            throw new ForbiddenException();
        }

        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var tracked = await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException(NotFound);
            var now = time.GetUtcNow();
            if (assigneeId is { } who
                && await store.FindParticipantAsync(conversationId, who, cancellationToken).ConfigureAwait(false) is var existing and not { IsActive: true })
            {
                // The assignee joins with the whole history visible.
                store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), tracked, ConversationEventKind.MemberAdded, userId, who, null, now));
                if (existing is not null)
                {
                    existing.Rejoin(ParticipantRole.Member, 1, now);
                }
                else
                {
                    store.Add(ConversationParticipant.Join(Guid.CreateVersion7(now), conversationId, who, ParticipantRole.Member, 1, now));
                }
            }

            if (tracked.Assign(assigneeId))
            {
                store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), tracked, ConversationEventKind.Assigned, userId, assigneeId, null, now));
            }

            updated = tracked;
        }, cancellationToken).ConfigureAwait(false);

        await NotifySupportAsync(updated!, cancellationToken).ConfigureAwait(false);
        return await SupportItemAsync(userId, updated!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes or reopens a website conversation (support staff, or a responder who takes part).</summary>
    public async Task<SupportItemView> SetSupportStatusAsync(Guid userId, Guid conversationId, SupportStatus status, CancellationToken cancellationToken)
    {
        await RequireSupportConversationAsync(userId, conversationId, cancellationToken, requireManage: true).ConfigureAwait(false);
        var updated = await ChangeSupportStatusAsync(userId, conversationId, status, cancellationToken).ConfigureAwait(false);
        return await SupportItemAsync(userId, updated, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A visitor writing into a resolved or pending conversation opens it again.</summary>
    public async Task ReopenOnVisitorMessageAsync(Guid visitorUserId, Guid conversationId, CancellationToken cancellationToken)
    {
        if (await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false) is { SupportStatus: SupportStatus.Closed or SupportStatus.Pending })
        {
            await ChangeSupportStatusAsync(visitorUserId, conversationId, SupportStatus.Open, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Conversation> ChangeSupportStatusAsync(Guid actorUserId, Guid conversationId, SupportStatus status, CancellationToken cancellationToken)
    {
        Conversation? updated = null;
        await MutateAsync(async () =>
        {
            var tracked = await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException(NotFound);
            if (tracked.SetSupportStatus(status))
            {
                var now = time.GetUtcNow();
                store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), tracked, ConversationEventKind.StatusChanged, actorUserId, null,
                    status switch { SupportStatus.Closed => "closed", SupportStatus.Pending => "pending", _ => "open" }, now));
            }

            updated = tracked;
        }, cancellationToken).ConfigureAwait(false);

        await NotifySupportAsync(updated!, cancellationToken).ConfigureAwait(false);
        return updated!;
    }

    private static bool IsSupportStaff(OrganizationMember membership) =>
        membership.IsActive && (membership.IsSupportAgent || membership.Can(OrganizationPermission.Manage));

    private sealed record SupportAccess(bool CanEnter, bool CanReadAll, bool CanManageAll, bool CanReadParticipating);

    private async Task<SupportAccess> GetSupportAccessAsync(Guid userId, Guid organizationId, OrganizationMember membership, CancellationToken cancellationToken)
    {
        var legacyStaff = IsSupportStaff(membership);
        if (scopedAccess is null) return new SupportAccess(true, legacyStaff, legacyStaff, true);

        var permissions = await scopedAccess.GetOrganizationPermissionsAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        var allowed = permissions.Allowed.ToHashSet(StringComparer.Ordinal);
        var denied = permissions.Denied.ToHashSet(StringComparer.Ordinal);
        var accessConfigured = await scopedAccess.IsPermissionConfiguredAsync(organizationId, "support.access", cancellationToken).ConfigureAwait(false);
        var readConfigured = await scopedAccess.IsPermissionConfiguredAsync(organizationId, "support.cases.read", cancellationToken).ConfigureAwait(false);
        var manageConfigured = await scopedAccess.IsPermissionConfiguredAsync(organizationId, "support.cases.manage", cancellationToken).ConfigureAwait(false);

        // An explicit denial of the support area is a hard stop, including for legacy agents.
        var canEnter = !accessConfigured || allowed.Contains("support.access");
        var accessGrant = accessConfigured && allowed.Contains("support.access");
        var readGrant = allowed.Contains("support.cases.read");
        var manageGrant = allowed.Contains("support.cases.manage");
        var canManageAll = canEnter && (manageConfigured ? manageGrant : legacyStaff);
        var canReadAll = canEnter && (readConfigured ? readGrant : accessGrant || manageGrant || legacyStaff);
        // Explicit access deny blocks any support participation path, even if it is an active case participant.
        if (denied.Contains("support.access")) canEnter = false;
        var canReadParticipating = canEnter && (!readConfigured || readGrant);
        return new SupportAccess(canEnter, canReadAll, canManageAll, canReadParticipating);
    }

    private async Task<bool> IsSupportAgentAsync(Guid organizationId, Guid userId, OrganizationMember membership, CancellationToken cancellationToken)
    {
        var access = await GetSupportAccessAsync(userId, organizationId, membership, cancellationToken).ConfigureAwait(false);
        return access.CanEnter && (access.CanManageAll || access.CanReadAll);
    }

    private async Task<IReadOnlyList<Guid>> ListSupportStaffIdsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var members = await organizations.ListActiveMembersAsync(organizationId, cancellationToken).ConfigureAwait(false);
        var staffIds = new List<Guid>();
        foreach (var member in members)
        {
            if (await IsSupportAgentAsync(organizationId, member.UserId, member, cancellationToken).ConfigureAwait(false))
            {
                staffIds.Add(member.UserId);
            }
        }

        return staffIds;
    }

    private async Task<(Conversation Conversation, bool FullInbox)> RequireSupportConversationAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken, bool requireManage = false)
    {
        var conversation = await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is not { Type: ConversationType.Website, OrganizationId: { } organizationId })
        {
            throw new ResourceNotFoundException(NotFound);
        }

        var membership = await organizations.FindMemberAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);
        var access = membership is { IsActive: true }
            ? await GetSupportAccessAsync(userId, organizationId, membership, cancellationToken).ConfigureAwait(false)
            : new SupportAccess(false, false, false, false);
        if (!access.CanEnter) throw new ResourceNotFoundException(NotFound);
        var fullInbox = access.CanReadAll;
        var takesPart = membership is { IsActive: true }
            && await store.FindParticipantAsync(conversationId, userId, cancellationToken).ConfigureAwait(false) is { IsActive: true };
        var canRead = fullInbox || (access.CanReadParticipating && takesPart);
        if (!canRead) throw new ResourceNotFoundException(NotFound);
        if (requireManage)
        {
            var manageConfigured = scopedAccess is not null
                && await scopedAccess.IsPermissionConfiguredAsync(organizationId, "support.cases.manage", cancellationToken).ConfigureAwait(false);
            var legacyManage = membership is not null && (IsSupportStaff(membership) || takesPart);
            var canManage = access.CanManageAll || (!manageConfigured && legacyManage);
            if (!canManage) throw new ForbiddenException();
        }
        return (conversation, fullInbox);
    }

    private async Task<SupportItemView> SupportItemAsync(Guid userId, Conversation conversation, CancellationToken cancellationToken)
    {
        var me = await store.FindParticipantAsync(conversation.Id, userId, cancellationToken).ConfigureAwait(false);
        var participating = me is { IsActive: true };
        var unread = participating ? await store.CountUnreadAsync(conversation.Id, userId, me!.LastReadSequence, cancellationToken).ConfigureAwait(false) : 0;
        var assignee = conversation.AssignedToUserId is { } a
            ? (await directory.GetSummariesAsync([a], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(a)
            : null;
        return new SupportItemView(conversation, participating, unread, assignee);
    }

    /// <summary>Participants reload the conversation; support staff of the organization reload their inbox.</summary>
    private async Task NotifySupportAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        await NotifyParticipantsAsync(conversation, "updated", [], cancellationToken).ConfigureAwait(false);
        if (conversation.OrganizationId is { } organizationId)
        {
            var staff = await ListSupportStaffIdsAsync(organizationId, cancellationToken).ConfigureAwait(false);
            await NotifyAsync(staff, conversation, "inbox", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Replays the support inbox notification for a committed routing event. Duplicate delivery is
    /// safe: clients reload the current case from the database rather than applying event deltas.
    /// </summary>
    public async Task NotifySupportRoutingAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is { Type: ConversationType.Website })
        {
            await NotifySupportAsync(conversation, cancellationToken).ConfigureAwait(false);
        }
    }
}
