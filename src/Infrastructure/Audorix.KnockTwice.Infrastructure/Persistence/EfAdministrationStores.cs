using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Application.Users;
using Audorix.KnockTwice.Domain.Administration;
using Audorix.KnockTwice.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Audorix.KnockTwice.Infrastructure.Persistence;

internal sealed class EfAuditStore(ChatXDbContext db) : IAuditStore
{
    public void Add(AuditEntry entry) => db.AuditEntries.Add(entry);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        var entries = Filter(query);
        var ordered = query.OldestFirst
            ? entries.OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.Id)
            : entries.OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id);
        return await ordered.Skip(query.Skip).Take(query.Limit).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<int> CountAsync(AuditQuery query, CancellationToken cancellationToken) => Filter(query).CountAsync(cancellationToken);

    private IQueryable<AuditEntry> Filter(AuditQuery query)
    {
        var entries = db.AuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim().ToLowerInvariant();
            entries = action.EndsWith('.') ? entries.Where(a => a.Action.StartsWith(action)) : entries.Where(a => a.Action == action || a.Action.StartsWith(action + "."));
        }

        if (query.ActorUserId is { } actor)
        {
            entries = entries.Where(a => a.ActorUserId == actor);
        }

        if (!string.IsNullOrWhiteSpace(query.TargetId))
        {
            var target = query.TargetId.Trim();
            entries = entries.Where(a => a.TargetId == target);
        }

        if (query.Succeeded is { } succeeded)
        {
            entries = entries.Where(a => a.Succeeded == succeeded);
        }

        if (query.Before is { } before)
        {
            entries = entries.Where(a => a.OccurredAtUtc < before);
        }

        if (query.From is { } from)
        {
            entries = entries.Where(a => a.OccurredAtUtc >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(a => a.OccurredAtUtc <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Action, target and the actor's display name; ToLower() is translated to SQL LOWER().
            var text = query.Search.Trim().ToLowerInvariant();
#pragma warning disable CA1304, CA1311, CA1862
            var actors = db.UserProfiles.Where(p => p.DisplayName.ToLower().Contains(text)).Select(p => (Guid?)p.UserId);
            entries = entries.Where(a => a.Action.Contains(text)
                || (a.TargetType != null && a.TargetType.ToLower().Contains(text))
                || (a.TargetId != null && a.TargetId.ToLower().Contains(text))
                || actors.Contains(a.ActorUserId));
#pragma warning restore CA1304, CA1311, CA1862
        }

        return entries;
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batch, CancellationToken cancellationToken)
    {
        var ids = await db.AuditEntries.AsNoTracking().Where(a => a.OccurredAtUtc < cutoff).OrderBy(a => a.OccurredAtUtc).Select(a => a.Id).Take(batch)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.Count == 0 ? 0 : await db.AuditEntries.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class EfInstallationSettingsStore(ChatXDbContext db) : IInstallationSettingsStore
{
    public async Task<IReadOnlyList<InstallationSetting>> ListAsync(CancellationToken cancellationToken) =>
        await db.InstallationSettings.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task SaveAsync(IReadOnlyDictionary<string, string?> values, Guid? byUserId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var keys = values.Keys.ToList();
        var existing = await db.InstallationSettings.Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, cancellationToken).ConfigureAwait(false);
        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var setting))
            {
                if (value is null)
                {
                    db.InstallationSettings.Remove(setting);
                }
                else
                {
                    setting.Change(value, nowUtc, byUserId);
                }
            }
            else if (value is not null)
            {
                db.InstallationSettings.Add(InstallationSetting.Create(key, value, nowUtc, byUserId));
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class EfRegistrationInvitations(ChatXDbContext db, TimeProvider time) : IRegistrationInvitations
{
    public async Task<bool> HasOpenInvitationAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = OrganizationInvitation.Normalize(email);
        var now = time.GetUtcNow();
        var invitations = await db.OrganizationInvitations.AsNoTracking()
            .Where(i => i.NormalizedEmail == normalized && i.Status == InvitationStatus.Pending)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return invitations.Any(i => i.IsOpen(now));
    }
}

// ToLower() inside these queries is translated to SQL LOWER(), not executed with a .NET culture.
#pragma warning disable CA1304, CA1311, CA1862

/// <summary>Administration read model. Pages are small, so per-page facts (sessions, organizations) are computed in memory.</summary>
internal sealed class EfAdminDirectory(ChatXDbContext db, TimeProvider time) : IAdminDirectory
{
    public async Task<AdminUserPage> SearchUsersAsync(
        string? query, string? filter, int skip, int take, CancellationToken cancellationToken, string? role = null, string? sort = null)
    {
        var users = from u in db.Users.AsNoTracking()
                    where !u.IsVisitor && u.DeletedAtUtc == null
                    join p in db.UserProfiles.AsNoTracking() on u.Id equals p.UserId
                    select new { User = u, Profile = p };

        if (!string.IsNullOrWhiteSpace(query))
        {
            var upper = query.Trim().ToUpperInvariant();
            var lower = query.Trim().ToLowerInvariant();
            users = users.Where(x => x.User.NormalizedEmail!.Contains(upper) || x.Profile.DisplayName.ToLower().Contains(lower));
        }

        switch (filter?.ToLowerInvariant())
        {
            case null or "" or "all":
                break;
            case "enabled":
                users = users.Where(x => x.User.DisabledAtUtc == null);
                break;
            case "disabled":
                users = users.Where(x => x.User.DisabledAtUtc != null);
                break;
            case "unconfirmed":
                users = users.Where(x => !x.User.EmailConfirmed);
                break;
            default:
                var filterRole = Domain.Authorization.Roles.Active.FirstOrDefault(r => string.Equals(r, filter, StringComparison.OrdinalIgnoreCase)) ?? filter;
                users = users.Where(x => db.UserRoles.Any(ur => ur.UserId == x.User.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == filterRole)));
                break;
        }

        // Redesign 2026-11: the role is its own filter; "none" means accounts without an installation role.
        if (!string.IsNullOrWhiteSpace(role) && !string.Equals(role, "all", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(role, "none", StringComparison.OrdinalIgnoreCase))
            {
                users = users.Where(x => !db.UserRoles.Any(ur => ur.UserId == x.User.Id));
            }
            else
            {
                var name = Domain.Authorization.Roles.Active.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)) ?? role;
                users = users.Where(x => db.UserRoles.Any(ur => ur.UserId == x.User.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == name)));
            }
        }

        var total = await users.CountAsync(cancellationToken).ConfigureAwait(false);
        var ordered = sort switch
        {
            "nameDesc" => users.OrderByDescending(x => x.Profile.DisplayName).ThenByDescending(x => x.User.Id),
            _ => users.OrderBy(x => x.Profile.DisplayName).ThenBy(x => x.User.Id),
        };
        var page = await ordered.Skip(skip).Take(take).Select(x => x.User.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new AdminUserPage(await DescribeUsersAsync(page, cancellationToken).ConfigureAwait(false), total);
    }

    public async Task<AdminUserView?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        (await DescribeUsersAsync([userId], cancellationToken).ConfigureAwait(false)).SingleOrDefault();

    public async Task<AdminOrganizationPage> SearchOrganizationsAsync(string? query, int skip, int take, CancellationToken cancellationToken, string? sort = null)
    {
        var organizations = db.Organizations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var lower = query.Trim().ToLowerInvariant();
            organizations = organizations.Where(o => o.Name.ToLower().Contains(lower));
        }

        var total = await organizations.CountAsync(cancellationToken).ConfigureAwait(false);
        var ordered = sort switch
        {
            "nameDesc" => organizations.OrderByDescending(o => o.Name).ThenByDescending(o => o.Id),
            "members" => organizations
                .OrderByDescending(o => db.OrganizationMembers.Count(m => m.OrganizationId == o.Id && m.Status == MembershipStatus.Active))
                .ThenBy(o => o.Name).ThenBy(o => o.Id),
            _ => organizations.OrderBy(o => o.Name).ThenBy(o => o.Id),
        };
        var page = await ordered.Skip(skip).Take(take).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = page.Select(o => o.Id).ToList();
        var members = await db.OrganizationMembers.AsNoTracking()
            .Where(m => ids.Contains(m.OrganizationId) && m.Status == MembershipStatus.Active)
            .Select(m => new { m.OrganizationId, m.Role })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new AdminOrganizationPage(
            page.Select(o => new AdminOrganizationView(
                o.Id, o.Name, o.CreatedAtUtc,
                members.Count(m => m.OrganizationId == o.Id),
                members.Count(m => m.OrganizationId == o.Id && m.Role == OrganizationRole.Owner),
                o.PhotoKey is null ? null : o.PhotoVersion)).ToList(),
            total);
    }

    public async Task<AdminOrganizationDetail?> FindOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, cancellationToken).ConfigureAwait(false);
        if (organization is null)
        {
            return null;
        }

        var members = await (
            from m in db.OrganizationMembers.AsNoTracking()
            where m.OrganizationId == organizationId && m.Status == MembershipStatus.Active
            join p in db.UserProfiles.AsNoTracking() on m.UserId equals p.UserId
            join u in db.Users.AsNoTracking() on m.UserId equals u.Id
            select new { Member = m, Profile = p, u.Email }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var views = members
            .Select(x => new AdminMemberView(
                new UserSummary(x.Profile.UserId, x.Profile.DisplayName, x.Profile.Initials, PhotoVersion: x.Profile.HasPhoto && !x.Profile.IsDeleted ? x.Profile.PhotoVersion : 0,
                    GeneratedAvatarStyle: x.Profile.IsDeleted ? null : x.Profile.GeneratedAvatarStyle, GeneratedAvatarSeed: x.Profile.IsDeleted ? null : x.Profile.GeneratedAvatarSeed),
                x.Email ?? string.Empty, x.Member.Role, x.Member.JoinedAtUtc))
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.User.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new AdminOrganizationDetail(
            new AdminOrganizationView(organization.Id, organization.Name, organization.CreatedAtUtc, views.Count, views.Count(v => v.Role == OrganizationRole.Owner),
                organization.PhotoKey is null ? null : organization.PhotoVersion),
            views);
    }

    public Task<int> CountEnabledInRoleAsync(string role, CancellationToken cancellationToken) =>
        (from ur in db.UserRoles
         join r in db.Roles on ur.RoleId equals r.Id
         join u in db.Users on ur.UserId equals u.Id
         where r.Name == role && u.DisabledAtUtc == null
         select u.Id).CountAsync(cancellationToken);

    private async Task<IReadOnlyList<AdminUserView>> DescribeUsersAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await (
            from u in db.Users.AsNoTracking()
            where ids.Contains(u.Id) && !u.IsVisitor && u.DeletedAtUtc == null
            join p in db.UserProfiles.AsNoTracking() on u.Id equals p.UserId
            select new { User = u, Profile = p }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var roles = await (
            from ur in db.UserRoles.AsNoTracking()
            where ids.Contains(ur.UserId)
            join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            select new { ur.UserId, r.Name }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var sessions = await db.UserSessions.AsNoTracking().Where(s => ids.Contains(s.UserId))
            .Select(s => new { s.UserId, s.CreatedAtUtc, s.RevokedAtUtc, s.ExpiresAtUtc })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var memberships = await db.OrganizationMembers.AsNoTracking().Where(m => ids.Contains(m.UserId) && m.Status == MembershipStatus.Active)
            .Select(m => m.UserId).ToListAsync(cancellationToken).ConfigureAwait(false);

        var now = time.GetUtcNow();
        var byId = rows.ToDictionary(r => r.User.Id);
        return ids.Where(byId.ContainsKey).Select(id =>
        {
            var row = byId[id];
            var mine = sessions.Where(s => s.UserId == id).ToList();
            return new AdminUserView(
                id,
                row.User.Email ?? string.Empty,
                row.Profile.DisplayName,
                row.Profile.Initials,
                row.User.EmailConfirmed,
                row.User.DisabledAtUtc is not null,
                row.User.DisabledAtUtc is null && row.User.LockoutEnd is { } end && end > now,
                roles.Where(r => r.UserId == id && r.Name is not null).Select(r => r.Name!).Order(StringComparer.Ordinal).ToList(),
                row.Profile.CreatedAtUtc,
                mine.Count == 0 ? null : mine.Max(s => s.CreatedAtUtc),
                mine.Count(s => s.RevokedAtUtc is null && s.ExpiresAtUtc > now),
                memberships.Count(m => m == id),
                row.Profile.HasPhoto ? row.Profile.PhotoVersion : 0);
        }).ToList();
    }
}
#pragma warning restore CA1304, CA1311, CA1862
