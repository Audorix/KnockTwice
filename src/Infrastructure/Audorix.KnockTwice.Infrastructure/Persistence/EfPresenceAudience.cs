using Audorix.KnockTwice.Application.Realtime;
using Audorix.KnockTwice.Domain.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Audorix.KnockTwice.Infrastructure.Persistence;

/// <summary>
/// Presence audience (owner decision of 2026-11-23, Phase 22, ADR-036): the user's contacts plus
/// everyone they share an active direct chat or group with, minus anyone involved in a block with
/// them in either direction. Organization membership alone no longer counts, and neither do channels
/// or website conversations: their size is unbounded, and announcing everyone to everyone grows with
/// the square of it. The relationship is symmetric, so the same query answers "who sees me" and
/// "whom may I see".
/// </summary>
internal sealed class EfPresenceAudience(ChatXDbContext db) : IPresenceAudience
{
    public async Task<IReadOnlyList<Guid>> GetAudienceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => c.UserLowId == userId || c.UserHighId == userId)
            .Select(c => c.UserLowId == userId ? c.UserHighId : c.UserLowId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var chatPartners = await (
            from mine in db.ConversationParticipants
            where mine.UserId == userId && mine.Status == ParticipantStatus.Active
            join conversation in db.Conversations on mine.ConversationId equals conversation.Id
            where conversation.Type == ConversationType.Direct || conversation.Type == ConversationType.Group
            join other in db.ConversationParticipants on mine.ConversationId equals other.ConversationId
            where other.Status == ParticipantStatus.Active && other.UserId != userId && !other.IsVisitor
            select other.UserId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);

        var blocked = await db.UserBlocks.AsNoTracking()
            .Where(b => b.BlockerId == userId || b.BlockedId == userId)
            .Select(b => b.BlockerId == userId ? b.BlockedId : b.BlockerId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return contacts.Concat(chatPartners).Distinct().Except(blocked).ToList();
    }
}
