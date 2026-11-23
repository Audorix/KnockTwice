using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Domain;
using Audorix.ChatX.Domain.Organizations;

namespace Audorix.ChatX.Application.Organizations;

public sealed record WorkspaceView(Workspace Workspace, WorkspaceRole? MyRole);

public sealed record WorkspaceMemberView(WorkspaceMember Member, UserSummary User);

/// <summary>
/// Workspaces are always resolved inside an organization the caller belongs to. Workspace access
/// requires active workspace membership, except for organization members holding
/// <see cref="OrganizationPermission.ManageWorkspaces"/>. Inaccessible workspaces are "not found".
/// </summary>
public sealed class WorkspaceService(IOrganizationStore store, OrganizationService organizations, IUserDirectory directory, TimeProvider time)
{
    public async Task<IReadOnlyList<WorkspaceView>> ListAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var (_, membership) = await organizations.RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        var mine = (await store.ListWorkspaceMembershipsAsync(organizationId, userId, cancellationToken).ConfigureAwait(false))
            .Where(m => m.IsActive)
            .ToDictionary(m => m.WorkspaceId, m => m.Role);
        var seeAll = membership.Can(OrganizationPermission.ManageWorkspaces);
        return (await store.ListWorkspacesAsync(organizationId, cancellationToken).ConfigureAwait(false))
            .Where(w => seeAll || mine.ContainsKey(w.Id))
            .Select(w => new WorkspaceView(w, mine.TryGetValue(w.Id, out var role) ? role : null))
            .OrderBy(w => w.Workspace.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<WorkspaceView> CreateAsync(Guid userId, Guid organizationId, string? name, CancellationToken cancellationToken)
    {
        var (_, membership) = await organizations.RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        OrganizationService.RequirePermission(membership, OrganizationPermission.ManageWorkspaces);
        var now = time.GetUtcNow();
        var workspace = Workspace.Create(Guid.CreateVersion7(now), organizationId, name ?? string.Empty, now);
        store.Add(workspace);
        store.Add(WorkspaceMember.Join(Guid.CreateVersion7(now), workspace, membership, WorkspaceRole.Administrator, now));
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return new WorkspaceView(workspace, WorkspaceRole.Administrator);
    }

    public async Task<IReadOnlyList<WorkspaceMemberView>> ListMembersAsync(Guid userId, Guid organizationId, Guid workspaceId, CancellationToken cancellationToken)
    {
        var (workspace, _, _) = await RequireAccessAsync(userId, organizationId, workspaceId, cancellationToken).ConfigureAwait(false);
        var members = await store.ListActiveWorkspaceMembersAsync(workspace.Id, cancellationToken).ConfigureAwait(false);
        var users = await directory.GetSummariesAsync(members.Select(m => m.UserId).ToList(), cancellationToken).ConfigureAwait(false);
        return members
            .Where(m => users.ContainsKey(m.UserId))
            .Select(m => new WorkspaceMemberView(m, users[m.UserId]))
            .OrderByDescending(m => m.Member.Role)
            .ThenBy(m => m.User.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Adds an active organization member to the workspace (workspace or organization administrators).</summary>
    public async Task<WorkspaceMemberView> AddMemberAsync(
        Guid userId, Guid organizationId, Guid workspaceId, Guid targetUserId, WorkspaceRole role, CancellationToken cancellationToken)
    {
        var (workspace, _, canManage) = await RequireAccessAsync(userId, organizationId, workspaceId, cancellationToken).ConfigureAwait(false);
        if (!canManage)
        {
            throw new ForbiddenException();
        }

        var organizationMembership = await store.FindMemberAsync(organizationId, targetUserId, cancellationToken).ConfigureAwait(false);
        if (organizationMembership is not { IsActive: true })
        {
            throw new DomainRuleException("not_organization_member", "Workspace members must be active members of the workspace's organization.");
        }

        var now = time.GetUtcNow();
        var existing = await store.FindWorkspaceMemberAsync(workspace.Id, targetUserId, cancellationToken).ConfigureAwait(false);
        if (existing is { IsActive: true })
        {
            throw new ConflictException("already_member");
        }

        var member = existing ?? WorkspaceMember.Join(Guid.CreateVersion7(now), workspace, organizationMembership, role, now);
        if (existing is null)
        {
            store.Add(member);
        }
        else
        {
            existing.Reactivate(organizationMembership, role, now);
        }

        await SaveAsync(cancellationToken, "already_member").ConfigureAwait(false);
        var user = (await directory.GetSummariesAsync([targetUserId], cancellationToken).ConfigureAwait(false))[targetUserId];
        return new WorkspaceMemberView(member, user);
    }

    /// <summary>Removes a member (workspace or organization administrators), or leaves (any member).</summary>
    public async Task RemoveMemberAsync(Guid userId, Guid organizationId, Guid workspaceId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var (workspace, _, canManage) = await RequireAccessAsync(userId, organizationId, workspaceId, cancellationToken).ConfigureAwait(false);
        if (targetUserId != userId && !canManage)
        {
            throw new ForbiddenException();
        }

        var member = await store.FindWorkspaceMemberAsync(workspace.Id, targetUserId, cancellationToken).ConfigureAwait(false);
        if (member is not { IsActive: true })
        {
            throw new ResourceNotFoundException("member_not_found");
        }

        member.Revoke(time.GetUtcNow());
        foreach (var team in await store.ListTeamMembershipsAsync(workspace.Id, targetUserId, cancellationToken).ConfigureAwait(false))
        {
            store.Remove(team);
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renames a workspace (workspace or organization administrators).</summary>
    public async Task<WorkspaceView> RenameAsync(Guid userId, Guid organizationId, Guid workspaceId, string? name, CancellationToken cancellationToken)
    {
        var (workspace, membership, canManage) = await RequireAccessAsync(userId, organizationId, workspaceId, cancellationToken).ConfigureAwait(false);
        if (!canManage)
        {
            throw new ForbiddenException();
        }

        workspace.Rename(name ?? string.Empty);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return new WorkspaceView(workspace, membership is { IsActive: true } ? membership.Role : null);
    }

    /// <summary>
    /// Changes a workspace member's role (workspace or organization administrators). A workspace
    /// keeps at least one administrator unless an organization administrator makes the change.
    /// </summary>
    public async Task<WorkspaceMemberView> ChangeRoleAsync(
        Guid userId, Guid organizationId, Guid workspaceId, Guid targetUserId, WorkspaceRole role, CancellationToken cancellationToken)
    {
        var (workspace, _, canManage) = await RequireAccessAsync(userId, organizationId, workspaceId, cancellationToken).ConfigureAwait(false);
        if (!canManage)
        {
            throw new ForbiddenException();
        }

        var member = await store.FindWorkspaceMemberAsync(workspace.Id, targetUserId, cancellationToken).ConfigureAwait(false);
        if (member is not { IsActive: true })
        {
            throw new ResourceNotFoundException("member_not_found");
        }

        member.ChangeRole(role);

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        var user = (await directory.GetSummariesAsync([targetUserId], cancellationToken).ConfigureAwait(false))[targetUserId];
        return new WorkspaceMemberView(member, user);
    }

    /// <summary>The caller's view of a workspace: whether they can manage it (teams, channels, members).</summary>
    public async Task<(Workspace Workspace, WorkspaceMember? Membership, bool CanManage)> RequireWorkspaceAccessAsync(
        Guid userId, Guid organizationId, Guid workspaceId, CancellationToken cancellationToken) =>
        await RequireAccessAsync(userId, organizationId, workspaceId, cancellationToken).ConfigureAwait(false);

    private async Task<(Workspace Workspace, WorkspaceMember? Membership, bool CanManage)> RequireAccessAsync(
        Guid userId, Guid organizationId, Guid workspaceId, CancellationToken cancellationToken)
    {
        var (_, organizationMembership) = await organizations.RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        var workspace = await store.FindWorkspaceAsync(organizationId, workspaceId, cancellationToken).ConfigureAwait(false);
        var membership = workspace is null ? null : await store.FindWorkspaceMemberAsync(workspace.Id, userId, cancellationToken).ConfigureAwait(false);
        var orgAdmin = organizationMembership.Can(OrganizationPermission.ManageWorkspaces);
        if (workspace is null || (membership is not { IsActive: true } && !orgAdmin))
        {
            throw new ResourceNotFoundException("workspace_not_found");
        }

        var canManage = orgAdmin || membership is { IsActive: true, Role: WorkspaceRole.Administrator };
        return (workspace, membership, canManage);
    }

    private async Task SaveAsync(CancellationToken cancellationToken, string code = "conflict")
    {
        if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException(code);
        }
    }
}
