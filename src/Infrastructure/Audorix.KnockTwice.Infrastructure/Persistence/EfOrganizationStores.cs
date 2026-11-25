using Audorix.KnockTwice.Application.Contacts;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Application.Organizations;
using Audorix.KnockTwice.Application.Users;
using Audorix.KnockTwice.Domain.Contacts;
using Audorix.KnockTwice.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Infrastructure.Persistence;

internal static class SaveHelper
{
    /// <summary>
    /// Saves; a rejected write (typically a unique index losing a race) discards the unit of work and
    /// returns false so the use case can answer with a conflict instead of a server error.
    /// </summary>
    public static async Task<bool> TrySaveChangesAsync(this ChatXDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}

internal sealed class EfOrganizationStore(ChatXDbContext db) : IOrganizationStore
{
    public void Add(Organization organization) => db.Organizations.Add(organization);

    public void Add(OrganizationMember member) => db.OrganizationMembers.Add(member);

    public void Add(OrganizationInvitation invitation) => db.OrganizationInvitations.Add(invitation);

    public void Add(Workspace workspace) => db.Workspaces.Add(workspace);

    public void Add(WorkspaceMember member) => db.WorkspaceMembers.Add(member);

    public Task<Organization?> FindOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Organizations.SingleOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

    public Task<OrganizationMember?> FindMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        db.OrganizationMembers.SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<(Organization Organization, OrganizationMember Membership)>> ListActiveMembershipsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var rows = await (
            from m in db.OrganizationMembers
            join o in db.Organizations on m.OrganizationId equals o.Id
            where m.UserId == userId && m.Status == MembershipStatus.Active
            select new { o, m }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => (r.o, r.m)).ToList();
    }

    public async Task<IReadOnlyList<OrganizationMember>> ListActiveMembersAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.Status == MembershipStatus.Active)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<OrganizationInvitation?> FindInvitationAsync(Guid invitationId, CancellationToken cancellationToken) =>
        db.OrganizationInvitations.SingleOrDefaultAsync(i => i.Id == invitationId, cancellationToken);

    public async Task<IReadOnlyList<OrganizationInvitation>> ListPendingInvitationsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.OrganizationInvitations
            .Where(i => i.OrganizationId == organizationId && i.Status == InvitationStatus.Pending)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<OrganizationInvitation>> ListPendingInvitationsForEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        await db.OrganizationInvitations
            .Where(i => i.NormalizedEmail == normalizedEmail && i.Status == InvitationStatus.Pending)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<Workspace?> FindWorkspaceAsync(Guid organizationId, Guid workspaceId, CancellationToken cancellationToken) =>
        db.Workspaces.SingleOrDefaultAsync(w => w.OrganizationId == organizationId && w.Id == workspaceId, cancellationToken);

    public async Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.Workspaces.Where(w => w.OrganizationId == organizationId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<WorkspaceMember>> ListWorkspaceMembershipsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        await db.WorkspaceMembers
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<WorkspaceMember?> FindWorkspaceMemberAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        db.WorkspaceMembers.SingleOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<WorkspaceMember>> ListActiveWorkspaceMembersAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        await db.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId && m.Status == MembershipStatus.Active)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlySet<Guid>> ListActiveWorkspaceIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Select(m => m.WorkspaceId)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

    public void Add(Team team) => db.Teams.Add(team);

    public void Remove(Team team) => db.Teams.Remove(team);

    public void Add(TeamMember member) => db.TeamMembers.Add(member);

    public void Remove(TeamMember member) => db.TeamMembers.Remove(member);

    public void Add(ChannelTeam link) => db.ChannelTeams.Add(link);

    public void Remove(ChannelTeam link) => db.ChannelTeams.Remove(link);

    public void Add(OrganizationGuest guest) => db.OrganizationGuests.Add(guest);

    public void Add(GuestInvitation invitation) => db.GuestInvitations.Add(invitation);

    public Task<Team?> FindTeamAsync(Guid teamId, CancellationToken cancellationToken) =>
        db.Teams.SingleOrDefaultAsync(t => t.Id == teamId, cancellationToken);

    public async Task<IReadOnlyList<Team>> ListTeamsAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        await db.Teams.Where(t => t.WorkspaceId == workspaceId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<TeamMember>> ListTeamMembersAsync(Guid teamId, CancellationToken cancellationToken) =>
        await db.TeamMembers.Where(m => m.TeamId == teamId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<TeamMember?> FindTeamMemberAsync(Guid teamId, Guid userId, CancellationToken cancellationToken) =>
        db.TeamMembers.SingleOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<TeamMember>> ListTeamMembershipsAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        await db.TeamMembers.Where(m => m.WorkspaceId == workspaceId && m.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ChannelTeam>> ListChannelTeamsAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await db.ChannelTeams.Where(l => l.ConversationId == conversationId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ChannelTeam>> ListTeamChannelsAsync(Guid teamId, CancellationToken cancellationToken) =>
        await db.ChannelTeams.Where(l => l.TeamId == teamId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<OrganizationGuest?> FindGuestAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        db.OrganizationGuests.SingleOrDefaultAsync(g => g.OrganizationId == organizationId && g.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<OrganizationGuest>> ListActiveGuestsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.OrganizationGuests.Where(g => g.OrganizationId == organizationId && g.Status == MembershipStatus.Active)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Organization>> ListGuestOrganizationsAsync(Guid userId, CancellationToken cancellationToken) =>
        await (
            from g in db.OrganizationGuests
            where g.UserId == userId && g.Status == MembershipStatus.Active
            join o in db.Organizations on g.OrganizationId equals o.Id
            where o.GuestsEnabled
            select o).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<GuestInvitation?> FindGuestInvitationAsync(Guid invitationId, CancellationToken cancellationToken) =>
        db.GuestInvitations.SingleOrDefaultAsync(i => i.Id == invitationId, cancellationToken);

    public async Task<IReadOnlyList<GuestInvitation>> ListPendingGuestInvitationsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.GuestInvitations.Where(i => i.OrganizationId == organizationId && i.Status == InvitationStatus.Pending)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<GuestInvitation>> ListPendingGuestInvitationsForEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        await db.GuestInvitations.Where(i => i.NormalizedEmail == normalizedEmail && i.Status == InvitationStatus.Pending)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken) => db.TrySaveChangesAsync(cancellationToken);
}

internal sealed class EfContactStore(ChatXDbContext db) : IContactStore
{
    public Task<Contact?> FindContactAsync(UserPair pair, CancellationToken cancellationToken) =>
        db.Contacts.SingleOrDefaultAsync(c => c.UserLowId == pair.Low && c.UserHighId == pair.High, cancellationToken);

    public async Task<IReadOnlyList<Contact>> ListContactsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Contacts.Where(c => c.UserLowId == userId || c.UserHighId == userId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<ContactRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        db.ContactRequests.SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);

    public Task<ContactRequest?> FindPendingRequestAsync(UserPair pair, CancellationToken cancellationToken) =>
        db.ContactRequests.SingleOrDefaultAsync(
            r => r.PairLowId == pair.Low && r.PairHighId == pair.High && r.Status == ContactRequestStatus.Pending, cancellationToken);

    public async Task<IReadOnlyList<ContactRequest>> ListPendingRequestsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.ContactRequests
            .Where(r => (r.RequesterId == userId || r.RecipientId == userId) && r.Status == ContactRequestStatus.Pending)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<UserBlock?> FindBlockAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken) =>
        db.UserBlocks.SingleOrDefaultAsync(b => b.BlockerId == blockerId && b.BlockedId == blockedId, cancellationToken);

    public async Task<IReadOnlyList<UserBlock>> ListBlocksByAsync(Guid blockerId, CancellationToken cancellationToken) =>
        await db.UserBlocks.Where(b => b.BlockerId == blockerId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlySet<Guid>> ListBlockPartnersAsync(Guid userId, CancellationToken cancellationToken)
    {
        var partners = await db.UserBlocks
            .Where(b => b.BlockerId == userId || b.BlockedId == userId)
            .Select(b => b.BlockerId == userId ? b.BlockedId : b.BlockerId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return partners.ToHashSet();
    }

    public async Task<bool> AreContactsOrPendingAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken)
    {
        var pair = UserPair.Of(userId, otherUserId);
        return await db.Contacts.AnyAsync(c => c.UserLowId == pair.Low && c.UserHighId == pair.High, cancellationToken).ConfigureAwait(false)
            || await db.ContactRequests.AnyAsync(
                r => r.PairLowId == pair.Low && r.PairHighId == pair.High && r.Status == ContactRequestStatus.Pending, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<SharedOrganization>>> ListSharedOrganizationsAsync(
        Guid userId, IReadOnlyCollection<Guid> otherUserIds, CancellationToken cancellationToken)
    {
        if (otherUserIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<SharedOrganization>>();
        }

        var others = otherUserIds.ToList();
        var rows = await (
            from mine in db.OrganizationMembers.AsNoTracking()
            where mine.UserId == userId && mine.Status == MembershipStatus.Active
            join other in db.OrganizationMembers.AsNoTracking() on mine.OrganizationId equals other.OrganizationId
            where other.Status == MembershipStatus.Active && others.Contains(other.UserId)
            join organization in db.Organizations.AsNoTracking() on mine.OrganizationId equals organization.Id
            select new { other.UserId, organization.Id, organization.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(r => r.UserId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<SharedOrganization>)g.Select(r => new SharedOrganization(r.Id, r.Name)).OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    public void Add(Contact contact) => db.Contacts.Add(contact);

    public void Add(ContactRequest request) => db.ContactRequests.Add(request);

    public void Add(UserBlock block) => db.UserBlocks.Add(block);

    public void Remove(Contact contact) => db.Contacts.Remove(contact);

    public void Remove(UserBlock block) => db.UserBlocks.Remove(block);

    public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken) => db.TrySaveChangesAsync(cancellationToken);
}

/// <summary>Read model joining accounts, profiles and organization membership. Never returns email addresses.</summary>
internal sealed class EfUserDirectory(ChatXDbContext db, IOptions<AccountSettings> accounts) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, UserSummary>> GetSummariesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserSummary>();
        }

        var ids = userIds.Distinct().ToList();
        var profiles = await db.UserProfiles.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return profiles.ToDictionary(p => p.UserId, p => new UserSummary(p.UserId, p.DisplayName, p.Initials, p.IsDeleted, p.HasPhoto && !p.IsDeleted ? p.PhotoVersion : 0));
    }

    public async Task<Guid?> FindConfirmedUserIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        // ASP.NET Core Identity's default normalizer is upper-invariant.
        var normalized = email.Trim().ToUpperInvariant();
        var requireConfirmed = accounts.Value.RequireConfirmedEmail;
        var ids = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized && !u.IsVisitor && u.DeletedAtUtc == null && (u.EmailConfirmed || !requireConfirmed))
            .Select(u => u.Id)
            .Take(1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.Count == 1 ? ids[0] : null;
    }

    public Task<bool> IsActiveUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var requireConfirmed = accounts.Value.RequireConfirmedEmail;
        // Website visitors (Phase 10) are never people one can contact or add.
        return db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && !u.IsVisitor && u.DeletedAtUtc == null && (u.EmailConfirmed || !requireConfirmed), cancellationToken);
    }

    public async Task<IReadOnlyList<UserSummary>> SearchColleaguesAsync(Guid userId, string text, int limit, CancellationToken cancellationToken)
    {
        var lowered = text.ToLowerInvariant();
        var colleagues =
            from mine in db.OrganizationMembers
            where mine.UserId == userId && mine.Status == MembershipStatus.Active
            join other in db.OrganizationMembers on mine.OrganizationId equals other.OrganizationId
            where other.Status == MembershipStatus.Active && other.UserId != userId
            select other.UserId;

        // Translated to SQL lower() (ASCII-only in SQLite); culture-aware .NET overloads are not translatable.
#pragma warning disable CA1304, CA1311, CA1862
        var profiles = await db.UserProfiles.AsNoTracking()
            .Where(p => colleagues.Contains(p.UserId) && p.DisplayName.ToLower().Contains(lowered))
#pragma warning restore CA1304, CA1311, CA1862
            .OrderBy(p => p.DisplayName)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return profiles.Select(p => new UserSummary(p.UserId, p.DisplayName, p.Initials, PhotoVersion: p.HasPhoto ? p.PhotoVersion : 0)).ToList();
    }

    public Task<bool> SharesActiveOrganizationAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken) =>
        (from a in db.OrganizationMembers
         where a.UserId == userId && a.Status == MembershipStatus.Active
         join b in db.OrganizationMembers on a.OrganizationId equals b.OrganizationId
         where b.UserId == otherUserId && b.Status == MembershipStatus.Active
         select a.Id).AnyAsync(cancellationToken);
}
