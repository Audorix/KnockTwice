using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Domain.Conversations;
using Audorix.ChatX.Domain.Organizations;

namespace Audorix.ChatX.Application.Conversations;

/// <summary>One website conversation in the support inbox.</summary>
public sealed record SupportItemView(Conversation Conversation, bool Participating, int UnreadCount, UserSummary? Assignee);

/// <summary>
/// Support-agent foundations (Phase 11, owner decision "agent role + assignment"). Website
/// conversations have a status (open/closed) and at most one assigned agent. Support agents and
/// organization owners/administrators see the whole inbox and assign anyone eligible; responders who
/// take part in a conversation see and work that conversation. Someone who takes a conversation sees
/// its whole history (unlike groups): support needs the visitor's earlier messages.
/// </summary>
public sealed partial class ConversationService
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

        var fullInbox = IsSupportStaff(membership);
        var items = new List<(Conversation Conversation, bool Participating, int Unread)>();
        foreach (var conversation in await store.ListWebsiteConversationsAsync(organizationId, status, InboxLimit, cancellationToken).ConfigureAwait(false))
        {
            if (assignedToMe && conversation.AssignedToUserId != userId)
            {
                continue;
            }

            var me = await store.FindParticipantAsync(conversation.Id, userId, cancellationToken).ConfigureAwait(false);
            var participating = me is { IsActive: true };
            if (!fullInbox && !participating)
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

    /// <summary>Takes a conversation: assigns it to the caller, who joins it if needed.</summary>
    public Task<SupportItemView> ClaimAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        AssignAsync(userId, conversationId, userId, cancellationToken);

    /// <summary>
    /// Assigns (or, with null, unassigns) a website conversation. Support staff assign anyone who is
    /// support staff or already takes part; responders may only take it themselves.
    /// </summary>
    public async Task<SupportItemView> AssignAsync(Guid userId, Guid conversationId, Guid? assigneeId, CancellationToken cancellationToken)
    {
        var (conversation, fullInbox) = await RequireSupportConversationAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var organizationId = conversation.OrganizationId!.Value;
        if (assigneeId is { } target)
        {
            if (target != userId && !fullInbox)
            {
                throw new ForbiddenException();
            }

            var member = await organizations.FindMemberAsync(organizationId, target, cancellationToken).ConfigureAwait(false);
            var takesPart = await store.FindParticipantAsync(conversationId, target, cancellationToken).ConfigureAwait(false) is { IsActive: true };
            if (member is not { IsActive: true } || (!IsSupportStaff(member) && !takesPart))
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
        await RequireSupportConversationAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        var updated = await ChangeSupportStatusAsync(userId, conversationId, status, cancellationToken).ConfigureAwait(false);
        return await SupportItemAsync(userId, updated, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A visitor writing into a closed conversation reopens it.</summary>
    public async Task ReopenOnVisitorMessageAsync(Guid visitorUserId, Guid conversationId, CancellationToken cancellationToken)
    {
        if (await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false) is { SupportStatus: SupportStatus.Closed })
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
                    status == SupportStatus.Closed ? "closed" : "open", now));
            }

            updated = tracked;
        }, cancellationToken).ConfigureAwait(false);

        await NotifySupportAsync(updated!, cancellationToken).ConfigureAwait(false);
        return updated!;
    }

    private static bool IsSupportStaff(OrganizationMember membership) =>
        membership.IsActive && (membership.IsSupportAgent || membership.Can(OrganizationPermission.Manage));

    private async Task<(Conversation Conversation, bool FullInbox)> RequireSupportConversationAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is not { Type: ConversationType.Website, OrganizationId: { } organizationId })
        {
            throw new ResourceNotFoundException(NotFound);
        }

        var membership = await organizations.FindMemberAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);
        var fullInbox = membership is not null && IsSupportStaff(membership);
        var takesPart = membership is { IsActive: true }
            && await store.FindParticipantAsync(conversationId, userId, cancellationToken).ConfigureAwait(false) is { IsActive: true };
        return fullInbox || takesPart ? (conversation, fullInbox) : throw new ResourceNotFoundException(NotFound);
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
            var staff = (await organizations.ListActiveMembersAsync(organizationId, cancellationToken).ConfigureAwait(false))
                .Where(IsSupportStaff).Select(m => m.UserId).ToList();
            await NotifyAsync(staff, conversation, "inbox", cancellationToken).ConfigureAwait(false);
        }
    }
}
