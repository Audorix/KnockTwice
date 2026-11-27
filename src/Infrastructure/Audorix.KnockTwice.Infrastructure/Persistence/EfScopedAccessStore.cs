using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Domain.Authorization;
using Audorix.KnockTwice.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Audorix.KnockTwice.Infrastructure.Persistence;

internal sealed class EfScopedAccessStore(ChatXDbContext db) : IScopedAccessStore, IEffectivePermissionReader
{
    public async Task<IReadOnlyList<AccessRoleView>> ListRolesAsync(string scope, Guid? organizationId, Guid? workspaceId, CancellationToken cancellationToken)
    {
        // Keep nested permission materialization out of the SQL projection. SQLite cannot translate
        // the correlated collection projection reliably across supported EF Core versions.
        var roles = await db.AccessRoles.AsNoTracking()
            .Include(role => role.Permissions)
            .Where(role => role.Scope == scope && role.OrganizationId == organizationId && role.WorkspaceId == workspaceId)
            .OrderBy(role => role.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (roles.Count == 0) return [];

        var roleIds = roles.Select(role => role.Id).ToArray();
        var groupCounts = await db.AccessRoleGroupAssignments.AsNoTracking()
            .Where(assignment => roleIds.Contains(assignment.RoleId))
            .GroupBy(assignment => assignment.RoleId)
            .Select(grouping => new { RoleId = grouping.Key, Count = grouping.Count() })
            .ToDictionaryAsync(item => item.RoleId, item => item.Count, cancellationToken).ConfigureAwait(false);

        return roles.Select(role => new AccessRoleView(
                role,
                role.Permissions.Select(permission => new RolePermissionDecision(permission.PermissionKey, permission.Effect))
                    .OrderBy(permission => permission.Key).ToList(),
                groupCounts.GetValueOrDefault(role.Id)))
            .ToList();
    }

    public async Task<AccessRoleView?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await db.AccessRoles.Include(item => item.Permissions)
            .SingleOrDefaultAsync(item => item.Id == roleId, cancellationToken).ConfigureAwait(false);
        if (role is null) return null;
        return new AccessRoleView(role, role.Permissions.Select(permission => new RolePermissionDecision(permission.PermissionKey, permission.Effect)).OrderBy(permission => permission.Key).ToList(),
            await db.AccessRoleGroupAssignments.CountAsync(assignment => assignment.RoleId == roleId, cancellationToken).ConfigureAwait(false));
    }

    public Task<bool> RoleNameExistsAsync(string normalizedName, string scope, Guid? organizationId, Guid? workspaceId, Guid? exceptRoleId, CancellationToken cancellationToken) =>
        db.AccessRoles.AnyAsync(role => role.NormalizedName == normalizedName && role.Scope == scope && role.OrganizationId == organizationId
            && role.WorkspaceId == workspaceId && role.Id != exceptRoleId, cancellationToken);

    public async Task SaveRoleAsync(AccessRole role, CancellationToken cancellationToken)
    {
        if (db.Entry(role).State == EntityState.Detached) db.AccessRoles.Add(role);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteRoleAsync(AccessRole role, CancellationToken cancellationToken)
    {
        db.AccessRoles.Remove(role);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(user => user.Id == userId && user.DeletedAtUtc == null && !user.IsVisitor, cancellationToken);

    public Task<bool> HasGroupRoleAsync(Guid roleId, Guid groupId, CancellationToken cancellationToken) =>
        db.AccessRoleGroupAssignments.AnyAsync(assignment => assignment.RoleId == roleId && assignment.GroupId == groupId, cancellationToken);

    public async Task AssignRoleToGroupAsync(Guid roleId, Guid groupId, Guid actorId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        db.AccessRoleGroupAssignments.Add(AccessRoleGroupAssignment.Create(roleId, groupId, nowUtc, actorId));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveRoleFromGroupAsync(Guid roleId, Guid groupId, CancellationToken cancellationToken)
    {
        var assignment = await db.AccessRoleGroupAssignments.SingleAsync(item => item.RoleId == roleId && item.GroupId == groupId, cancellationToken).ConfigureAwait(false);
        db.AccessRoleGroupAssignments.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccessGroupView>> ListGroupsAsync(string scope, Guid? organizationId, Guid? workspaceId, CancellationToken cancellationToken)
    {
        var groups = await db.AccessGroups.AsNoTracking().Where(group => group.Scope == scope && group.OrganizationId == organizationId && group.WorkspaceId == workspaceId)
            .OrderBy(group => group.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return await ToGroupViewsAsync(groups, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccessGroupView?> FindGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await db.AccessGroups.SingleOrDefaultAsync(item => item.Id == groupId, cancellationToken).ConfigureAwait(false);
        if (group is null) return null;
        return (await ToGroupViewsAsync([group], cancellationToken).ConfigureAwait(false)).Single();
    }

    public Task<bool> GroupNameExistsAsync(string normalizedName, string scope, Guid? organizationId, Guid? workspaceId, Guid? exceptGroupId, CancellationToken cancellationToken) =>
        db.AccessGroups.AnyAsync(group => group.NormalizedName == normalizedName && group.Scope == scope && group.OrganizationId == organizationId
            && group.WorkspaceId == workspaceId && group.Id != exceptGroupId, cancellationToken);

    public async Task SaveGroupAsync(AccessGroup group, CancellationToken cancellationToken)
    {
        if (db.Entry(group).State == EntityState.Detached) db.AccessGroups.Add(group);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(AccessGroup group, CancellationToken cancellationToken)
    {
        db.AccessGroups.Remove(group);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> IsGroupMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken) =>
        db.AccessGroupMembers.AnyAsync(member => member.GroupId == groupId && member.UserId == userId, cancellationToken);

    public async Task AddGroupMemberAsync(Guid groupId, Guid userId, Guid actorId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var group = await db.AccessGroups.SingleAsync(item => item.Id == groupId, cancellationToken).ConfigureAwait(false);
        if (group.Scope == PermissionScopes.Organization && !await db.OrganizationMembers.AnyAsync(item => item.OrganizationId == group.OrganizationId && item.UserId == userId && item.Status == MembershipStatus.Active, cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("organization_member_not_found");
        if (group.Scope == PermissionScopes.Workspace && !await db.WorkspaceMembers.AnyAsync(item => item.WorkspaceId == group.WorkspaceId && item.UserId == userId && item.Status == MembershipStatus.Active, cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("workspace_member_not_found");
        db.AccessGroupMembers.Add(AccessGroupMember.Create(groupId, userId, nowUtc, actorId));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveGroupMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        var member = await db.AccessGroupMembers.SingleAsync(item => item.GroupId == groupId && item.UserId == userId, cancellationToken).ConfigureAwait(false);
        db.AccessGroupMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<EffectivePermissionSet> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var assignments = await (from member in db.AccessGroupMembers.AsNoTracking()
                                 where member.UserId == userId
                                 join groupRole in db.AccessRoleGroupAssignments.AsNoTracking() on member.GroupId equals groupRole.GroupId
                                 join scopedGroup in db.AccessGroups.AsNoTracking() on member.GroupId equals scopedGroup.Id
                                 where scopedGroup.Scope == PermissionScopes.Installation
                                 join role in db.AccessRoles.AsNoTracking() on groupRole.RoleId equals role.Id
                                 where role.Scope == PermissionScopes.Installation
                                 join permission in db.AccessRolePermissions.AsNoTracking() on groupRole.RoleId equals permission.RoleId
                                 select new PermissionDecisionRow(permission.PermissionKey, permission.Effect))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var configured = await (from groupRole in db.AccessRoleGroupAssignments.AsNoTracking()
                                join scopedGroup in db.AccessGroups.AsNoTracking() on groupRole.GroupId equals scopedGroup.Id
                                where scopedGroup.Scope == PermissionScopes.Installation
                                join role in db.AccessRoles.AsNoTracking() on groupRole.RoleId equals role.Id
                                where role.Scope == PermissionScopes.Installation
                                join permission in db.AccessRolePermissions.AsNoTracking() on role.Id equals permission.RoleId
                                select permission.PermissionKey)
            .Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        return BuildEffectivePermissions(assignments, configured);
    }

    public Task<bool> IsPermissionConfiguredAsync(Guid organizationId, string permissionKey, CancellationToken cancellationToken) =>
        db.AccessRoleGroupAssignments.AnyAsync(assignment =>
            db.AccessGroups.Any(group => group.Id == assignment.GroupId && group.Scope == PermissionScopes.Organization && group.OrganizationId == organizationId)
            && db.AccessRoles.Any(role => role.Id == assignment.RoleId && role.Scope == PermissionScopes.Organization && role.OrganizationId == organizationId
                && db.AccessRolePermissions.Any(permission => permission.RoleId == role.Id && permission.PermissionKey == permissionKey)), cancellationToken);

    public async Task<EffectivePermissionSet> GetOrganizationPermissionsAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var assignments = await (from member in db.AccessGroupMembers.AsNoTracking()
                                 where member.UserId == userId
                                 join groupRole in db.AccessRoleGroupAssignments.AsNoTracking() on member.GroupId equals groupRole.GroupId
                                 join scopedGroup in db.AccessGroups.AsNoTracking() on member.GroupId equals scopedGroup.Id
                                 where scopedGroup.Scope == PermissionScopes.Installation
                                    || (scopedGroup.Scope == PermissionScopes.Organization && scopedGroup.OrganizationId == organizationId
                                        && db.OrganizationMembers.Any(orgMember => orgMember.OrganizationId == organizationId && orgMember.UserId == userId && orgMember.Status == MembershipStatus.Active))
                                 join role in db.AccessRoles.AsNoTracking() on groupRole.RoleId equals role.Id
                                 where role.Scope == PermissionScopes.Installation
                                    || (role.Scope == PermissionScopes.Organization && role.OrganizationId == organizationId)
                                 join permission in db.AccessRolePermissions.AsNoTracking() on role.Id equals permission.RoleId
                                 select new PermissionDecisionRow(permission.PermissionKey, permission.Effect))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return BuildEffectivePermissions(assignments);
    }

    public Task<bool> IsWorkspacePermissionConfiguredAsync(Guid workspaceId, string permissionKey, CancellationToken cancellationToken) =>
        db.AccessRoleGroupAssignments.AnyAsync(assignment =>
            db.AccessGroups.Any(group => group.Id == assignment.GroupId && group.Scope == PermissionScopes.Workspace && group.WorkspaceId == workspaceId)
            && db.AccessRoles.Any(role => role.Id == assignment.RoleId && role.Scope == PermissionScopes.Workspace && role.WorkspaceId == workspaceId
                && db.AccessRolePermissions.Any(permission => permission.RoleId == role.Id && permission.PermissionKey == permissionKey)), cancellationToken);

    public async Task<EffectivePermissionSet> GetWorkspacePermissionsAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        var workspace = await db.Workspaces.AsNoTracking().Where(item => item.Id == workspaceId)
            .Select(item => new { item.Id, item.OrganizationId }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (workspace is null) return new EffectivePermissionSet([], []);

        var assignments = await (from member in db.AccessGroupMembers.AsNoTracking()
                                 where member.UserId == userId
                                 join groupRole in db.AccessRoleGroupAssignments.AsNoTracking() on member.GroupId equals groupRole.GroupId
                                 join scopedGroup in db.AccessGroups.AsNoTracking() on member.GroupId equals scopedGroup.Id
                                 where (scopedGroup.Scope == PermissionScopes.Installation)
                                    || (scopedGroup.Scope == PermissionScopes.Organization && scopedGroup.OrganizationId == workspace.OrganizationId
                                        && db.OrganizationMembers.Any(orgMember => orgMember.OrganizationId == workspace.OrganizationId && orgMember.UserId == userId && orgMember.Status == MembershipStatus.Active))
                                    || (scopedGroup.Scope == PermissionScopes.Workspace && scopedGroup.WorkspaceId == workspaceId
                                        && db.WorkspaceMembers.Any(workspaceMember => workspaceMember.WorkspaceId == workspaceId && workspaceMember.UserId == userId && workspaceMember.Status == MembershipStatus.Active))
                                 join role in db.AccessRoles.AsNoTracking() on groupRole.RoleId equals role.Id
                                 where role.Scope == PermissionScopes.Installation
                                    || (role.Scope == PermissionScopes.Organization && role.OrganizationId == workspace.OrganizationId)
                                    || (role.Scope == PermissionScopes.Workspace && role.WorkspaceId == workspaceId)
                                 join permission in db.AccessRolePermissions.AsNoTracking() on role.Id equals permission.RoleId
                                 select new PermissionDecisionRow(permission.PermissionKey, permission.Effect))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return BuildEffectivePermissions(assignments);
    }

    private static EffectivePermissionSet BuildEffectivePermissions(IEnumerable<PermissionDecisionRow> assignments, IReadOnlyList<string>? configured = null)
    {
        var denied = assignments.Where(item => item.Effect == PermissionEffect.Deny).Select(item => item.PermissionKey)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var deniedSet = denied.ToHashSet(StringComparer.Ordinal);
        var allowed = assignments.Where(item => item.Effect == PermissionEffect.Allow && !deniedSet.Contains(item.PermissionKey))
            .Select(item => item.PermissionKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return new EffectivePermissionSet(allowed, denied, configured ?? assignments.Select(item => item.PermissionKey).Distinct(StringComparer.Ordinal).ToList());
    }

    private sealed record PermissionDecisionRow(string PermissionKey, PermissionEffect Effect);

    private async Task<IReadOnlyList<AccessGroupView>> ToGroupViewsAsync(List<AccessGroup> groups, CancellationToken cancellationToken)
    {
        if (groups.Count == 0) return [];
        var ids = groups.Select(group => group.Id).ToArray();
        var members = await db.AccessGroupMembers.AsNoTracking().Where(member => ids.Contains(member.GroupId))
            .GroupBy(member => member.GroupId).Select(grouping => new { Id = grouping.Key, Users = grouping.Select(member => member.UserId).ToList() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var roles = await db.AccessRoleGroupAssignments.AsNoTracking().Where(assignment => ids.Contains(assignment.GroupId))
            .GroupBy(assignment => assignment.GroupId).Select(grouping => new { Id = grouping.Key, RoleIds = grouping.Select(assignment => assignment.RoleId).ToList() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return groups.Select(group => new AccessGroupView(
            group,
            members.FirstOrDefault(row => row.Id == group.Id)?.Users ?? [],
            roles.FirstOrDefault(row => row.Id == group.Id)?.RoleIds ?? []))
            .ToList();
    }
}
