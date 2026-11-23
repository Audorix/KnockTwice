using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Conversations;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Domain.Organizations;

namespace Audorix.ChatX.Application.Organizations;

public sealed record TeamView(Team Team, int MemberCount, TeamRole? MyRole, bool CanManage);

public sealed record TeamMemberView(TeamMember Member, UserSummary User);

/// <summary>
/// Teams (Phase 11, owner decision "teams as member groups"): named groups of members of one
/// workspace. Workspace administrators and organization owners/administrators create, rename and
/// delete teams; Team Managers manage their team's members. Teams grant channel membership only —
/// administration rights flow down from the organization and the workspace, never from a team.
/// Adding someone to a team adds them to the channels the team is linked to.
/// </summary>
public sealed class TeamService(IOrganizationStore store, WorkspaceService workspaces, ConversationService conversations, IUserDirectory directory, TimeProvider time)
{
    public async Task<IReadOnlyList<TeamView>> ListAsync(Guid userId, Guid organizationId, Guid workspaceId, CancellationToken cancellationToken)
    {
        var (workspace, _, canManageWorkspace) = await workspaces.RequireWorkspaceAccessAsync(userId, organizationId, workspaceId, cancellationToken).ConfigureAwait(false);
        var views = new List<TeamView>();
        foreach (var team in await store.ListTeamsAsync(workspace.Id, cancellationToken).ConfigureAwait(false))
        {
            var members = await store.ListTeamMembersAsync(team.Id, cancellationToken).ConfigureAwait(false);
            var mine = members.FirstOrDefault(m => m.UserId == userId);
            views.Add(new TeamView(team, members.Count, mine?.Role, canManageWorkspace || mine?.Role == TeamRole.Manager));
        }

        return views.OrderBy(v => v.Team.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<TeamView> CreateAsync(Guid userId, Guid organizationId, Guid workspaceId, string? name, string? description, CancellationToken cancellationToken)
    {
        var (workspace, _, canManageWorkspace) = await workspaces.RequireWorkspaceAccessAsync(userId, organizationId, workspaceId, cancellationToken).ConfigureAwait(false);
        if (!canManageWorkspace)
        {
            throw new ForbiddenException();
        }

        var team = Team.Create(Guid.CreateVersion7(time.GetUtcNow()), workspace, name ?? string.Empty, description, time.GetUtcNow());
        store.Add(team);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return new TeamView(team, 0, null, true);
    }

    public async Task<TeamView> UpdateAsync(
        Guid userId, Guid organizationId, Guid workspaceId, Guid teamId, string? name, string? description, CancellationToken cancellationToken)
    {
        var (team, mine, _) = await RequireManagedTeamAsync(userId, organizationId, workspaceId, teamId, cancellationToken).ConfigureAwait(false);
        if (name is not null)
        {
            team.Rename(name);
        }

        if (description is not null)
        {
            team.Describe(description);
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        var count = (await store.ListTeamMembersAsync(team.Id, cancellationToken).ConfigureAwait(false)).Count;
        return new TeamView(team, count, mine?.Role, true);
    }

    /// <summary>Deletes a team (workspace or organization administrators). Channel members it added stay.</summary>
    public async Task DeleteAsync(Guid userId, Guid organizationId, Guid workspaceId, Guid teamId, CancellationToken cancellationToken)
    {
        var (team, _, canManageWorkspace) = await RequireTeamAsync(userId, organizationId, workspaceId, teamId, cancellationToken).ConfigureAwait(false);
        if (!canManageWorkspace)
        {
            throw new ForbiddenException();
        }

        store.Remove(team);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TeamMemberView>> ListMembersAsync(Guid userId, Guid organizationId, Guid workspaceId, Guid teamId, CancellationToken cancellationToken)
    {
        var (team, _, _) = await RequireTeamAsync(userId, organizationId, workspaceId, teamId, cancellationToken).ConfigureAwait(false);
        var members = await store.ListTeamMembersAsync(team.Id, cancellationToken).ConfigureAwait(false);
        var users = await directory.GetSummariesAsync(members.Select(m => m.UserId).ToList(), cancellationToken).ConfigureAwait(false);
        return members.Where(m => users.ContainsKey(m.UserId))
            .Select(m => new TeamMemberView(m, users[m.UserId]))
            .OrderByDescending(m => m.Member.Role)
            .ThenBy(m => m.User.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Adds a workspace member (team managers and administrators); they join the team's linked channels.</summary>
    public async Task<TeamMemberView> AddMemberAsync(
        Guid userId, Guid organizationId, Guid workspaceId, Guid teamId, Guid targetUserId, TeamRole role, CancellationToken cancellationToken)
    {
        var (team, _, _) = await RequireManagedTeamAsync(userId, organizationId, workspaceId, teamId, cancellationToken).ConfigureAwait(false);
        if (await store.FindTeamMemberAsync(team.Id, targetUserId, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new ConflictException("already_member");
        }

        var workspaceMembership = await store.FindWorkspaceMemberAsync(team.WorkspaceId, targetUserId, cancellationToken).ConfigureAwait(false);
        if (workspaceMembership is not { IsActive: true })
        {
            throw new RequestValidationException("userId", "not_workspace_member");
        }

        var member = TeamMember.Join(Guid.CreateVersion7(time.GetUtcNow()), team, workspaceMembership, role, time.GetUtcNow());
        store.Add(member);
        await SaveAsync(cancellationToken, "already_member").ConfigureAwait(false);
        await conversations.AddToTeamChannelsAsync(userId, team.Id, targetUserId, cancellationToken).ConfigureAwait(false);
        var user = (await directory.GetSummariesAsync([targetUserId], cancellationToken).ConfigureAwait(false))[targetUserId];
        return new TeamMemberView(member, user);
    }

    public async Task<TeamMemberView> ChangeRoleAsync(
        Guid userId, Guid organizationId, Guid workspaceId, Guid teamId, Guid targetUserId, TeamRole role, CancellationToken cancellationToken)
    {
        var (team, _, _) = await RequireManagedTeamAsync(userId, organizationId, workspaceId, teamId, cancellationToken).ConfigureAwait(false);
        var member = await store.FindTeamMemberAsync(team.Id, targetUserId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("member_not_found");
        member.ChangeRole(role);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        var user = (await directory.GetSummariesAsync([targetUserId], cancellationToken).ConfigureAwait(false))[targetUserId];
        return new TeamMemberView(member, user);
    }

    /// <summary>Removes someone from the team (managers and administrators), or leaves (anyone). Their channels stay.</summary>
    public async Task RemoveMemberAsync(Guid userId, Guid organizationId, Guid workspaceId, Guid teamId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var (team, _, canManage) = await RequireTeamAsync(userId, organizationId, workspaceId, teamId, cancellationToken).ConfigureAwait(false);
        if (targetUserId != userId && !canManage)
        {
            throw new ForbiddenException();
        }

        var member = await store.FindTeamMemberAsync(team.Id, targetUserId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("member_not_found");
        store.Remove(member);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The team inside a workspace the caller can see; CanManage covers team managers and administrators.</summary>
    private async Task<(Team Team, TeamMember? Mine, bool CanManage)> RequireTeamAsync(
        Guid userId, Guid organizationId, Guid workspaceId, Guid teamId, CancellationToken cancellationToken)
    {
        var (workspace, _, canManageWorkspace) = await workspaces.RequireWorkspaceAccessAsync(userId, organizationId, workspaceId, cancellationToken).ConfigureAwait(false);
        var team = await store.FindTeamAsync(teamId, cancellationToken).ConfigureAwait(false);
        if (team is null || team.WorkspaceId != workspace.Id)
        {
            throw new ResourceNotFoundException("team_not_found");
        }

        var mine = await store.FindTeamMemberAsync(team.Id, userId, cancellationToken).ConfigureAwait(false);
        return (team, mine, canManageWorkspace || mine?.Role == TeamRole.Manager);
    }

    private async Task<(Team Team, TeamMember? Mine, bool CanManage)> RequireManagedTeamAsync(
        Guid userId, Guid organizationId, Guid workspaceId, Guid teamId, CancellationToken cancellationToken)
    {
        var result = await RequireTeamAsync(userId, organizationId, workspaceId, teamId, cancellationToken).ConfigureAwait(false);
        return result.CanManage ? result : throw new ForbiddenException();
    }

    private async Task SaveAsync(CancellationToken cancellationToken, string code = "conflict")
    {
        if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException(code);
        }
    }
}
