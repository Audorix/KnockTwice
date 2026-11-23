using Audorix.ChatX.Application.Realtime;
using Audorix.ChatX.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Audorix.ChatX.Infrastructure.Persistence;

/// <summary>
/// Presence audience: the user's contacts plus everyone sharing an active organization with
/// them, minus anyone involved in a block with them in either direction. The relationship is
/// symmetric, so the same query answers "who sees me" and "whom may I see".
/// </summary>
internal sealed class EfPresenceAudience(ChatXDbContext db) : IPresenceAudience
{
    public async Task<IReadOnlyList<Guid>> GetAudienceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => c.UserLowId == userId || c.UserHighId == userId)
            .Select(c => c.UserLowId == userId ? c.UserHighId : c.UserLowId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var colleagues = await (
            from mine in db.OrganizationMembers
            where mine.UserId == userId && mine.Status == MembershipStatus.Active
            join other in db.OrganizationMembers on mine.OrganizationId equals other.OrganizationId
            where other.Status == MembershipStatus.Active && other.UserId != userId
            select other.UserId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);

        var blocked = await db.UserBlocks.AsNoTracking()
            .Where(b => b.BlockerId == userId || b.BlockedId == userId)
            .Select(b => b.BlockerId == userId ? b.BlockedId : b.BlockerId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return contacts.Concat(colleagues).Distinct().Except(blocked).ToList();
    }
}
