using Audorix.ChatX.Domain.Conversations;

namespace Audorix.ChatX.Application.Conversations;

public sealed partial class ConversationService
{
    /// <summary>
    /// Account deletion (Phase 13): the person leaves every group, channel and support conversation
    /// they still take part in, with a normal "left" entry and, in groups, the usual owner
    /// succession. Direct conversations keep their participation so the other person still sees
    /// the history, now with "Deleted user"; nobody can write into them any more. A visitor's own
    /// website conversation stays with the organization.
    /// </summary>
    public async Task LeaveEverythingAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (var participation in await store.ListForUserAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            if (participation.Conversation.Type == ConversationType.Direct || participation.Participant.IsVisitor)
            {
                continue;
            }

            var conversationId = participation.Conversation.Id;
            Conversation? updated = null;
            await MutateAsync(async () =>
            {
                updated = null;
                var conversation = await store.FindAsync(conversationId, cancellationToken).ConfigureAwait(false);
                var me = await store.FindParticipantAsync(conversationId, userId, cancellationToken).ConfigureAwait(false);
                if (conversation is null || me is not { IsActive: true })
                {
                    return;
                }

                var now = time.GetUtcNow();
                var wasOwner = me.Role == ParticipantRole.Owner;
                me.Leave(now);
                store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.MemberLeft, userId, null, null, now));
                if (conversation.Type == ConversationType.Group && wasOwner)
                {
                    var remaining = (await store.ListActiveParticipantsAsync(conversationId, cancellationToken).ConfigureAwait(false))
                        .Where(p => p.UserId != userId)
                        .ToList();
                    if (remaining.Count > 0 && remaining.All(p => p.Role != ParticipantRole.Owner))
                    {
                        var successor = remaining.OrderBy(p => p.JoinedAtUtc).ThenBy(p => p.Id).First();
                        successor.ChangeRole(ParticipantRole.Owner);
                        store.Add(ConversationEvent.Record(Guid.CreateVersion7(now), conversation, ConversationEventKind.RoleChanged, userId, successor.UserId, "owner", now));
                    }
                }

                updated = conversation;
            }, cancellationToken).ConfigureAwait(false);

            if (updated is not null)
            {
                await EndAccessAsync(updated, userId, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
