using Audorix.KnockTwice.Domain.Authorization;
using Audorix.KnockTwice.Application.Common;

namespace Audorix.KnockTwice.Application.Administration;

public sealed record AccessRoleView(AccessRole Role, IReadOnlyList<RolePermissionDecision> Permissions, int Groups);
public sealed record AccessGroupView(AccessGroup Group, IReadOnlyList<Guid> MemberIds, IReadOnlyList<Guid> RoleIds);
public sealed record EffectivePermissionSet(
    IReadOnlyList<string> Allowed,
    IReadOnlyList<string> Denied,
    IReadOnlyList<string>? Configured = null);

/// <summary>Persistence and effective-access port for installation-scoped custom roles and groups.</summary>
public interface IScopedAccessStore
{
    Task<IReadOnlyList<AccessRoleView>> ListRolesAsync(string scope, Guid? organizationId, Guid? workspaceId, CancellationToken cancellationToken);
    Task<AccessRoleView?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken);
    Task<bool> RoleNameExistsAsync(string normalizedName, string scope, Guid? organizationId, Guid? workspaceId, Guid? exceptRoleId, CancellationToken cancellationToken);
    Task SaveRoleAsync(AccessRole role, CancellationToken cancellationToken);
    Task DeleteRoleAsync(AccessRole role, CancellationToken cancellationToken);
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> HasGroupRoleAsync(Guid roleId, Guid groupId, CancellationToken cancellationToken);
    Task AssignRoleToGroupAsync(Guid roleId, Guid groupId, Guid actorId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task RemoveRoleFromGroupAsync(Guid roleId, Guid groupId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessGroupView>> ListGroupsAsync(string scope, Guid? organizationId, Guid? workspaceId, CancellationToken cancellationToken);
    Task<AccessGroupView?> FindGroupAsync(Guid groupId, CancellationToken cancellationToken);
    Task<bool> GroupNameExistsAsync(string normalizedName, string scope, Guid? organizationId, Guid? workspaceId, Guid? exceptGroupId, CancellationToken cancellationToken);
    Task SaveGroupAsync(AccessGroup group, CancellationToken cancellationToken);
    Task DeleteGroupAsync(AccessGroup group, CancellationToken cancellationToken);
    Task<bool> IsGroupMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken);
    Task AddGroupMemberAsync(Guid groupId, Guid userId, Guid actorId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task RemoveGroupMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken);
    Task<EffectivePermissionSet> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> IsPermissionConfiguredAsync(Guid organizationId, string permissionKey, CancellationToken cancellationToken);
    Task<EffectivePermissionSet> GetOrganizationPermissionsAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken);
    Task<bool> IsWorkspacePermissionConfiguredAsync(Guid workspaceId, string permissionKey, CancellationToken cancellationToken);
    Task<EffectivePermissionSet> GetWorkspacePermissionsAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken);
}

/// <summary>Read port shared by HTTP authorization and the signed-in user's permission snapshot.</summary>
public interface IEffectivePermissionReader
{
    Task<EffectivePermissionSet> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken);
    Task<EffectivePermissionSet> GetOrganizationPermissionsAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken);
}

/// <summary>Admin operations for installation-level custom roles and groups. Every change is audited.</summary>
public sealed class ScopedAccessControlService(IScopedAccessStore store, AuditService audit, TimeProvider time)
{
    public Task<IReadOnlyList<AccessRoleView>> ListRolesAsync(CancellationToken ct) => store.ListRolesAsync(PermissionScopes.Installation, null, null, ct);
    public Task<IReadOnlyList<AccessGroupView>> ListGroupsAsync(CancellationToken ct) => store.ListGroupsAsync(PermissionScopes.Installation, null, null, ct);
    public Task<IReadOnlyList<AccessRoleView>> ListOrganizationRolesAsync(Guid organizationId, CancellationToken ct) => store.ListRolesAsync(PermissionScopes.Organization, organizationId, null, ct);
    public Task<IReadOnlyList<AccessGroupView>> ListOrganizationGroupsAsync(Guid organizationId, CancellationToken ct) => store.ListGroupsAsync(PermissionScopes.Organization, organizationId, null, ct);
    public Task<IReadOnlyList<AccessRoleView>> ListWorkspaceRolesAsync(Guid organizationId, Guid workspaceId, CancellationToken ct) => store.ListRolesAsync(PermissionScopes.Workspace, organizationId, workspaceId, ct);
    public Task<IReadOnlyList<AccessGroupView>> ListWorkspaceGroupsAsync(Guid organizationId, Guid workspaceId, CancellationToken ct) => store.ListGroupsAsync(PermissionScopes.Workspace, organizationId, workspaceId, ct);

    public async Task<AccessRoleView> CreateRoleAsync(Guid actorId, string? name, string? description, IReadOnlyList<RolePermissionDecision>? permissions, CancellationToken ct,
        string scope = PermissionScopes.Installation, Guid? organizationId = null, Guid? workspaceId = null)
    {
        var now = time.GetUtcNow();
        var role = AccessRole.Create(Guid.CreateVersion7(now), name, description, actorId, now, scope, organizationId, workspaceId);
        role.SetPermissions(permissions);
        if (await store.RoleNameExistsAsync(role.NormalizedName, scope, organizationId, workspaceId, null, ct).ConfigureAwait(false))
        {
            throw new ConflictException("role_name_taken");
        }

        await store.SaveRoleAsync(role, ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.AccessRoleCreated, actorId, "access_role", role.Id.ToString(),
            new Dictionary<string, object?> { ["name"] = role.Name, ["permissions"] = role.Permissions.Select(item => new { item.PermissionKey, Effect = item.Effect.ToString() }).ToArray() }, cancellationToken: ct).ConfigureAwait(false);
        return await RequireRoleAsync(role.Id, ct).ConfigureAwait(false);
    }

    public async Task<AccessRoleView> UpdateRoleAsync(Guid actorId, Guid roleId, string? name, string? description, IReadOnlyList<RolePermissionDecision>? permissions, CancellationToken ct,
        string scope = PermissionScopes.Installation, Guid? organizationId = null, Guid? workspaceId = null)
    {
        var role = await RequireRoleEntityAsync(roleId, scope, organizationId, workspaceId, ct).ConfigureAwait(false);
        role.Update(name, description, permissions);
        if (await store.RoleNameExistsAsync(role.NormalizedName, role.Scope, role.OrganizationId, role.WorkspaceId, role.Id, ct).ConfigureAwait(false))
        {
            throw new ConflictException("role_name_taken");
        }

        await store.SaveRoleAsync(role, ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.AccessRoleUpdated, actorId, "access_role", role.Id.ToString(),
            new Dictionary<string, object?> { ["name"] = role.Name, ["permissions"] = role.Permissions.Select(item => new { item.PermissionKey, Effect = item.Effect.ToString() }).ToArray() }, cancellationToken: ct).ConfigureAwait(false);
        return await RequireRoleAsync(role.Id, ct).ConfigureAwait(false);
    }

    public async Task DeleteRoleAsync(Guid actorId, Guid roleId, CancellationToken ct,
        string scope = PermissionScopes.Installation, Guid? organizationId = null, Guid? workspaceId = null)
    {
        var role = await RequireRoleEntityAsync(roleId, scope, organizationId, workspaceId, ct).ConfigureAwait(false);
        await store.DeleteRoleAsync(role, ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.AccessRoleDeleted, actorId, "access_role", roleId.ToString(),
            new Dictionary<string, object?> { ["name"] = role.Name }, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<AccessGroupView> CreateGroupAsync(Guid actorId, string? name, string? description, CancellationToken ct,
        string scope = PermissionScopes.Installation, Guid? organizationId = null, Guid? workspaceId = null)
    {
        var now = time.GetUtcNow();
        var group = AccessGroup.Create(Guid.CreateVersion7(now), name, description, actorId, now, scope, organizationId, workspaceId);
        if (await store.GroupNameExistsAsync(group.NormalizedName, scope, organizationId, workspaceId, null, ct).ConfigureAwait(false))
        {
            throw new ConflictException("group_name_taken");
        }

        await store.SaveGroupAsync(group, ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.AccessGroupCreated, actorId, "access_group", group.Id.ToString(),
            new Dictionary<string, object?> { ["name"] = group.Name }, cancellationToken: ct).ConfigureAwait(false);
        return await RequireGroupAsync(group.Id, ct).ConfigureAwait(false);
    }

    public async Task<AccessGroupView> UpdateGroupAsync(Guid actorId, Guid groupId, string? name, string? description, CancellationToken ct,
        string scope = PermissionScopes.Installation, Guid? organizationId = null, Guid? workspaceId = null)
    {
        var group = await RequireGroupEntityAsync(groupId, scope, organizationId, workspaceId, ct).ConfigureAwait(false);
        group.Update(name, description);
        if (await store.GroupNameExistsAsync(group.NormalizedName, group.Scope, group.OrganizationId, group.WorkspaceId, group.Id, ct).ConfigureAwait(false))
        {
            throw new ConflictException("group_name_taken");
        }

        await store.SaveGroupAsync(group, ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.AccessGroupUpdated, actorId, "access_group", group.Id.ToString(),
            new Dictionary<string, object?> { ["name"] = group.Name }, cancellationToken: ct).ConfigureAwait(false);
        return await RequireGroupAsync(group.Id, ct).ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(Guid actorId, Guid groupId, CancellationToken ct,
        string scope = PermissionScopes.Installation, Guid? organizationId = null, Guid? workspaceId = null)
    {
        var group = await RequireGroupEntityAsync(groupId, scope, organizationId, workspaceId, ct).ConfigureAwait(false);
        await store.DeleteGroupAsync(group, ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.AccessGroupDeleted, actorId, "access_group", groupId.ToString(),
            new Dictionary<string, object?> { ["name"] = group.Name }, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<AccessGroupView> AddMemberAsync(Guid actorId, Guid groupId, Guid userId, CancellationToken ct,
        string scope = PermissionScopes.Installation, Guid? organizationId = null, Guid? workspaceId = null)
    {
        var group = await RequireGroupEntityAsync(groupId, scope, organizationId, workspaceId, ct).ConfigureAwait(false);
        if (!await store.UserExistsAsync(userId, ct).ConfigureAwait(false)) throw new ResourceNotFoundException("user_not_found");
        if (!await store.IsGroupMemberAsync(groupId, userId, ct).ConfigureAwait(false))
        {
            await store.AddGroupMemberAsync(groupId, userId, actorId, time.GetUtcNow(), ct).ConfigureAwait(false);
            await audit.RecordAsync(AuditActions.AccessGroupMemberAdded, actorId, "access_group", groupId.ToString(),
                new Dictionary<string, object?> { ["user"] = userId.ToString() }, cancellationToken: ct).ConfigureAwait(false);
        }

        return await RequireGroupAsync(group.Id, ct).ConfigureAwait(false);
    }

    public async Task<AccessGroupView> RemoveMemberAsync(Guid actorId, Guid groupId, Guid userId, CancellationToken ct,
        string scope = PermissionScopes.Installation, Guid? organizationId = null, Guid? workspaceId = null)
    {
        var group = await RequireGroupEntityAsync(groupId, scope, organizationId, workspaceId, ct).ConfigureAwait(false);
        if (await store.IsGroupMemberAsync(groupId, userId, ct).ConfigureAwait(false))
        {
            await store.RemoveGroupMemberAsync(groupId, userId, ct).ConfigureAwait(false);
            await audit.RecordAsync(AuditActions.AccessGroupMemberRemoved, actorId, "access_group", groupId.ToString(),
                new Dictionary<string, object?> { ["user"] = userId.ToString() }, cancellationToken: ct).ConfigureAwait(false);
        }

        return await RequireGroupAsync(group.Id, ct).ConfigureAwait(false);
    }

    public async Task<AccessGroupView> AssignRoleToGroupAsync(Guid actorId, Guid groupId, Guid roleId, CancellationToken ct,
        string scope = PermissionScopes.Installation, Guid? organizationId = null, Guid? workspaceId = null)
    {
        var group = await RequireGroupEntityAsync(groupId, scope, organizationId, workspaceId, ct).ConfigureAwait(false);
        var role = await RequireRoleEntityAsync(roleId, scope, organizationId, workspaceId, ct).ConfigureAwait(false);
        if (!await store.HasGroupRoleAsync(roleId, groupId, ct).ConfigureAwait(false))
        {
            await store.AssignRoleToGroupAsync(roleId, groupId, actorId, time.GetUtcNow(), ct).ConfigureAwait(false);
            await audit.RecordAsync(AuditActions.AccessRoleAssigned, actorId, "access_group", groupId.ToString(),
                new Dictionary<string, object?> { ["role"] = role.Name, ["group"] = group.Name }, cancellationToken: ct).ConfigureAwait(false);
        }

        return await RequireGroupAsync(groupId, ct).ConfigureAwait(false);
    }

    public async Task<AccessGroupView> RemoveRoleFromGroupAsync(Guid actorId, Guid groupId, Guid roleId, CancellationToken ct,
        string scope = PermissionScopes.Installation, Guid? organizationId = null, Guid? workspaceId = null)
    {
        var group = await RequireGroupEntityAsync(groupId, scope, organizationId, workspaceId, ct).ConfigureAwait(false);
        var role = await RequireRoleEntityAsync(roleId, scope, organizationId, workspaceId, ct).ConfigureAwait(false);
        if (await store.HasGroupRoleAsync(roleId, groupId, ct).ConfigureAwait(false))
        {
            await store.RemoveRoleFromGroupAsync(roleId, groupId, ct).ConfigureAwait(false);
            await audit.RecordAsync(AuditActions.AccessRoleUnassigned, actorId, "access_group", groupId.ToString(),
                new Dictionary<string, object?> { ["role"] = role.Name, ["group"] = group.Name }, cancellationToken: ct).ConfigureAwait(false);
        }

        return await RequireGroupAsync(groupId, ct).ConfigureAwait(false);
    }

    public Task<EffectivePermissionSet> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct) => store.GetEffectivePermissionsAsync(userId, ct);

    private async Task<AccessRoleView> RequireRoleAsync(Guid id, CancellationToken ct) =>
        await store.FindRoleAsync(id, ct).ConfigureAwait(false) ?? throw new ResourceNotFoundException("access_role_not_found");
    private async Task<AccessRole> RequireRoleEntityAsync(Guid id, string scope, Guid? organizationId, Guid? workspaceId, CancellationToken ct)
    {
        var role = (await store.FindRoleAsync(id, ct).ConfigureAwait(false))?.Role;
        return role is not null && role.Scope == scope && role.OrganizationId == organizationId && role.WorkspaceId == workspaceId
            ? role : throw new ResourceNotFoundException("access_role_not_found");
    }
    private async Task<AccessGroupView> RequireGroupAsync(Guid id, CancellationToken ct) =>
        await store.FindGroupAsync(id, ct).ConfigureAwait(false) ?? throw new ResourceNotFoundException("access_group_not_found");
    private async Task<AccessGroup> RequireGroupEntityAsync(Guid id, string scope, Guid? organizationId, Guid? workspaceId, CancellationToken ct)
    {
        var group = (await store.FindGroupAsync(id, ct).ConfigureAwait(false))?.Group;
        return group is not null && group.Scope == scope && group.OrganizationId == organizationId && group.WorkspaceId == workspaceId
            ? group : throw new ResourceNotFoundException("access_group_not_found");
    }
}
