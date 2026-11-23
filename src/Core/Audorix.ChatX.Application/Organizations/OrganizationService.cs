using System.Net.Mail;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Domain.Organizations;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Organizations;

/// <summary>Persistence port for organizations, memberships, invitations and workspaces.</summary>
public interface IOrganizationStore
{
    void Add(Organization organization);

    void Add(OrganizationMember member);

    void Add(OrganizationInvitation invitation);

    void Add(Workspace workspace);

    void Add(WorkspaceMember member);

    Task<Organization?> FindOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The membership record (active or revoked), or null.</summary>
    Task<OrganizationMember?> FindMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<(Organization Organization, OrganizationMember Membership)>> ListActiveMembershipsAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationMember>> ListActiveMembersAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<OrganizationInvitation?> FindInvitationAsync(Guid invitationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationInvitation>> ListPendingInvitationsAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationInvitation>> ListPendingInvitationsForEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<Workspace?> FindWorkspaceAsync(Guid organizationId, Guid workspaceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkspaceMember>> ListWorkspaceMembershipsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    Task<WorkspaceMember?> FindWorkspaceMemberAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkspaceMember>> ListActiveWorkspaceMembersAsync(Guid workspaceId, CancellationToken cancellationToken);

    /// <summary>Workspaces (of any organization) in which the user is an active member.</summary>
    Task<IReadOnlySet<Guid>> ListActiveWorkspaceIdsAsync(Guid userId, CancellationToken cancellationToken);

    // ---- Teams, channel links and guests (Phase 11) ------------------------------------------

    void Add(Team team);

    void Remove(Team team);

    void Add(TeamMember member);

    void Remove(TeamMember member);

    void Add(ChannelTeam link);

    void Remove(ChannelTeam link);

    void Add(OrganizationGuest guest);

    void Add(GuestInvitation invitation);

    Task<Team?> FindTeamAsync(Guid teamId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Team>> ListTeamsAsync(Guid workspaceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamMember>> ListTeamMembersAsync(Guid teamId, CancellationToken cancellationToken);

    Task<TeamMember?> FindTeamMemberAsync(Guid teamId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The user's team rows in a workspace (removed when they leave it).</summary>
    Task<IReadOnlyList<TeamMember>> ListTeamMembershipsAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChannelTeam>> ListChannelTeamsAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChannelTeam>> ListTeamChannelsAsync(Guid teamId, CancellationToken cancellationToken);

    Task<OrganizationGuest?> FindGuestAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationGuest>> ListActiveGuestsAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Organizations in which the user is an active guest and guests are enabled.</summary>
    Task<IReadOnlyList<Organization>> ListGuestOrganizationsAsync(Guid userId, CancellationToken cancellationToken);

    Task<GuestInvitation?> FindGuestInvitationAsync(Guid invitationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GuestInvitation>> ListPendingGuestInvitationsAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GuestInvitation>> ListPendingGuestInvitationsForEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>Saves; returns false (and discards pending changes) when a uniqueness rule rejected a concurrent duplicate.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Organization feature settings (decision register "Registration and organization creation").</summary>
public sealed class OrganizationSettings
{
    public const string SectionName = "Organizations";

    /// <summary>Whether any signed-in user may create organizations.</summary>
    public bool AllowCreation { get; set; } = true;

    /// <summary>Name of the workspace created with every organization.</summary>
    public string DefaultWorkspaceName { get; set; } = "General";
}

public sealed record OrganizationView(Organization Organization, OrganizationRole MyRole, bool IsSupportAgent = false);

public sealed record MemberView(OrganizationMember Member, UserSummary User);

public sealed record MembersView(IReadOnlyList<MemberView> Members, IReadOnlyList<OrganizationInvitation>? PendingInvitations);

public sealed record MyInvitationView(OrganizationInvitation Invitation, string OrganizationName, UserSummary? InvitedBy);

/// <summary>
/// Organization use cases with resource-based authorization. The organization id from the
/// request is never trusted on its own: every operation first resolves the caller's active
/// membership server-side. Non-members get "not found", so ids from other organizations
/// disclose nothing (Architecture §6).
/// </summary>
public sealed class OrganizationService(
    IOrganizationStore store,
    IUserDirectory directory,
    IUserAccountManager accounts,
    TimeProvider time,
    IOptionsSnapshot<OrganizationSettings> settings)
{
    public async Task<OrganizationView> CreateAsync(Guid userId, string? name, CancellationToken cancellationToken)
    {
        // Owner decision (Phase 9): when creation is restricted, installation administrators still create organizations.
        if (!settings.Value.AllowCreation
            && (await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false))?.Roles.Contains(Domain.Authorization.Roles.InstallationAdministrator) != true)
        {
            throw new FeatureDisabledException("organization_creation_disabled");
        }

        var now = time.GetUtcNow();
        var organization = Organization.Create(Guid.CreateVersion7(now), name ?? string.Empty, userId, now);
        var owner = OrganizationMember.Join(Guid.CreateVersion7(now), organization.Id, userId, OrganizationRole.Owner, now);
        var workspace = Workspace.Create(Guid.CreateVersion7(now), organization.Id, settings.Value.DefaultWorkspaceName, now);
        store.Add(organization);
        store.Add(owner);
        store.Add(workspace);
        store.Add(WorkspaceMember.Join(Guid.CreateVersion7(now), workspace, owner, WorkspaceRole.Administrator, now));
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        return new OrganizationView(organization, OrganizationRole.Owner);
    }

    public async Task<IReadOnlyList<OrganizationView>> ListMineAsync(Guid userId, CancellationToken cancellationToken) =>
        (await store.ListActiveMembershipsAsync(userId, cancellationToken).ConfigureAwait(false))
            .Select(m => new OrganizationView(m.Organization, m.Membership.Role, m.Membership.IsSupportAgent))
            .OrderBy(o => o.Organization.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public async Task<OrganizationView> GetAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var (organization, membership) = await RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        return new OrganizationView(organization, membership.Role, membership.IsSupportAgent);
    }

    public async Task<OrganizationView> RenameAsync(Guid userId, Guid organizationId, string? name, CancellationToken cancellationToken)
    {
        var (organization, membership) = await RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        RequirePermission(membership, OrganizationPermission.Manage);
        organization.Rename(name ?? string.Empty, time.GetUtcNow());
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        return new OrganizationView(organization, membership.Role);
    }

    /// <summary>Organization-specific configuration (Phase 11): guests and who creates channels. Owners and administrators.</summary>
    public async Task<OrganizationView> ConfigureAsync(
        Guid userId, Guid organizationId, bool? guestsEnabled, ChannelCreationPolicy? channelCreation, CancellationToken cancellationToken)
    {
        var (organization, membership) = await RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        RequirePermission(membership, OrganizationPermission.Manage);
        organization.Configure(guestsEnabled, channelCreation, time.GetUtcNow());
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        return new OrganizationView(organization, membership.Role, membership.IsSupportAgent);
    }

    /// <summary>Grants or removes the Support Agent role (owners and administrators).</summary>
    public async Task<MemberView> SetSupportAgentAsync(Guid userId, Guid organizationId, Guid targetUserId, bool isAgent, CancellationToken cancellationToken)
    {
        var (_, membership) = await RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        RequirePermission(membership, OrganizationPermission.ManageMembers);
        var target = await RequireActiveMemberAsync(organizationId, targetUserId, cancellationToken).ConfigureAwait(false);
        target.SetSupportAgent(isAgent);
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        var user = (await directory.GetSummariesAsync([targetUserId], cancellationToken).ConfigureAwait(false))[targetUserId];
        return new MemberView(target, user);
    }

    /// <summary>Active members for any member; pending invitations only for members who manage membership.</summary>
    public async Task<MembersView> ListMembersAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var (_, membership) = await RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        var members = await store.ListActiveMembersAsync(organizationId, cancellationToken).ConfigureAwait(false);
        var users = await directory.GetSummariesAsync(members.Select(m => m.UserId).ToList(), cancellationToken).ConfigureAwait(false);
        var views = members
            .Where(m => users.ContainsKey(m.UserId))
            .Select(m => new MemberView(m, users[m.UserId]))
            .OrderByDescending(m => m.Member.Role)
            .ThenBy(m => m.User.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        IReadOnlyList<OrganizationInvitation>? invitations = null;
        if (membership.Can(OrganizationPermission.ManageMembers))
        {
            var now = time.GetUtcNow();
            invitations = (await store.ListPendingInvitationsAsync(organizationId, cancellationToken).ConfigureAwait(false))
                .Where(i => i.IsOpen(now))
                .OrderBy(i => i.Email, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return new MembersView(views, invitations);
    }

    /// <summary>
    /// Invites an email address. The response is identical whether or not the address has an
    /// account, so inviting cannot be used to discover accounts.
    /// </summary>
    public async Task<OrganizationInvitation> InviteAsync(Guid userId, Guid organizationId, string? email, OrganizationRole role, CancellationToken cancellationToken)
    {
        var (_, membership) = await RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        RequirePermission(membership, OrganizationPermission.ManageMembers);
        var address = email?.Trim() ?? string.Empty;
        if (!MailAddress.TryCreate(address, out var parsed) || parsed.Address != address || !address.Contains('.', StringComparison.Ordinal))
        {
            throw new RequestValidationException("email", "email_invalid");
        }

        var now = time.GetUtcNow();
        var normalized = OrganizationInvitation.Normalize(address);
        if ((await store.ListPendingInvitationsAsync(organizationId, cancellationToken).ConfigureAwait(false))
            .Any(i => i.NormalizedEmail == normalized && i.IsOpen(now)))
        {
            throw new ConflictException("invitation_pending");
        }

        var invitation = OrganizationInvitation.Create(Guid.CreateVersion7(now), organizationId, address, role, userId, now);
        store.Add(invitation);
        await SaveOrConflictAsync(cancellationToken, "invitation_pending").ConfigureAwait(false);
        return invitation;
    }

    public async Task RevokeInvitationAsync(Guid userId, Guid organizationId, Guid invitationId, CancellationToken cancellationToken)
    {
        var (_, membership) = await RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        RequirePermission(membership, OrganizationPermission.ManageMembers);
        var invitation = await store.FindInvitationAsync(invitationId, cancellationToken).ConfigureAwait(false);
        if (invitation is null || invitation.OrganizationId != organizationId || !invitation.IsOpen(time.GetUtcNow()))
        {
            throw new ResourceNotFoundException("invitation_not_found");
        }

        invitation.Revoke(time.GetUtcNow());
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Open invitations addressed to the caller's confirmed email address.</summary>
    public async Task<IReadOnlyList<MyInvitationView>> ListMyInvitationsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is not { EmailConfirmed: true })
        {
            return [];
        }

        var now = time.GetUtcNow();
        var invitations = (await store.ListPendingInvitationsForEmailAsync(OrganizationInvitation.Normalize(account.Email), cancellationToken).ConfigureAwait(false))
            .Where(i => i.IsOpen(now))
            .ToList();
        var inviters = await directory.GetSummariesAsync(invitations.Select(i => i.InvitedByUserId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        var result = new List<MyInvitationView>();
        foreach (var invitation in invitations)
        {
            var organization = await store.FindOrganizationAsync(invitation.OrganizationId, cancellationToken).ConfigureAwait(false);
            if (organization is not null)
            {
                result.Add(new MyInvitationView(invitation, organization.Name, inviters.GetValueOrDefault(invitation.InvitedByUserId)));
            }
        }

        return result;
    }

    public async Task<OrganizationView> AcceptInvitationAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken)
    {
        var invitation = await RequireOwnInvitationAsync(userId, invitationId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var organization = await store.FindOrganizationAsync(invitation.OrganizationId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("invitation_not_found");
        var membership = await store.FindMemberAsync(invitation.OrganizationId, userId, cancellationToken).ConfigureAwait(false);
        if (membership is { IsActive: true })
        {
            invitation.Accept(now);
            await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
            return new OrganizationView(organization, membership.Role);
        }

        if (membership is null)
        {
            membership = OrganizationMember.Join(Guid.CreateVersion7(now), invitation.OrganizationId, userId, invitation.Role, now);
            store.Add(membership);
        }
        else
        {
            membership.Reactivate(invitation.Role, now);
        }

        invitation.Accept(now);
        await SaveOrConflictAsync(cancellationToken, "already_member").ConfigureAwait(false);
        return new OrganizationView(organization, membership.Role);
    }

    public async Task DeclineInvitationAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken)
    {
        var invitation = await RequireOwnInvitationAsync(userId, invitationId, cancellationToken).ConfigureAwait(false);
        invitation.Decline(time.GetUtcNow());
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Owners change roles. The last active owner cannot be demoted.</summary>
    public async Task<MemberView> ChangeRoleAsync(Guid userId, Guid organizationId, Guid targetUserId, OrganizationRole role, CancellationToken cancellationToken)
    {
        var (_, membership) = await RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        RequirePermission(membership, OrganizationPermission.ManageRoles);
        var target = await RequireActiveMemberAsync(organizationId, targetUserId, cancellationToken).ConfigureAwait(false);
        if (target.Role == OrganizationRole.Owner && role != OrganizationRole.Owner)
        {
            await RequireAnotherOwnerAsync(organizationId, targetUserId, cancellationToken).ConfigureAwait(false);
        }

        target.ChangeRole(role);
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        var user = (await directory.GetSummariesAsync([targetUserId], cancellationToken).ConfigureAwait(false))[targetUserId];
        return new MemberView(target, user);
    }

    /// <summary>
    /// Removes a member (or leaves, when the target is the caller). Removing an owner requires the
    /// owner role; the last owner can neither leave nor be removed. Workspace memberships in the
    /// organization are revoked at the same time.
    /// </summary>
    public async Task RemoveMemberAsync(Guid userId, Guid organizationId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var (_, membership) = await RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        var target = targetUserId == userId ? membership : await RequireActiveMemberAsync(organizationId, targetUserId, cancellationToken).ConfigureAwait(false);
        if (targetUserId != userId)
        {
            RequirePermission(membership, target.Role == OrganizationRole.Owner ? OrganizationPermission.ManageRoles : OrganizationPermission.ManageMembers);
        }

        if (target.Role == OrganizationRole.Owner)
        {
            await RequireAnotherOwnerAsync(organizationId, targetUserId, cancellationToken).ConfigureAwait(false);
        }

        var now = time.GetUtcNow();
        target.Revoke(now);
        foreach (var workspaceMembership in await store.ListWorkspaceMembershipsAsync(organizationId, targetUserId, cancellationToken).ConfigureAwait(false))
        {
            workspaceMembership.Revoke(now);
            foreach (var team in await store.ListTeamMembershipsAsync(workspaceMembership.WorkspaceId, targetUserId, cancellationToken).ConfigureAwait(false))
            {
                store.Remove(team);
            }
        }

        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- Installation administration (Phase 9): no membership needed; the same owner rules apply.

    public async Task RenameAsAdministratorAsync(Guid organizationId, string? name, CancellationToken cancellationToken)
    {
        var organization = await store.FindOrganizationAsync(organizationId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("organization_not_found");
        organization.Rename(name ?? string.Empty, time.GetUtcNow());
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangeRoleAsAdministratorAsync(Guid organizationId, Guid targetUserId, OrganizationRole role, CancellationToken cancellationToken)
    {
        var target = await RequireActiveMemberAsync(organizationId, targetUserId, cancellationToken).ConfigureAwait(false);
        if (target.Role == OrganizationRole.Owner && role != OrganizationRole.Owner)
        {
            await RequireAnotherOwnerAsync(organizationId, targetUserId, cancellationToken).ConfigureAwait(false);
        }

        target.ChangeRole(role);
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveMemberAsAdministratorAsync(Guid organizationId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var target = await RequireActiveMemberAsync(organizationId, targetUserId, cancellationToken).ConfigureAwait(false);
        if (target.Role == OrganizationRole.Owner)
        {
            await RequireAnotherOwnerAsync(organizationId, targetUserId, cancellationToken).ConfigureAwait(false);
        }

        var now = time.GetUtcNow();
        target.Revoke(now);
        foreach (var workspaceMembership in await store.ListWorkspaceMembershipsAsync(organizationId, targetUserId, cancellationToken).ConfigureAwait(false))
        {
            workspaceMembership.Revoke(now);
            foreach (var team in await store.ListTeamMembershipsAsync(workspaceMembership.WorkspaceId, targetUserId, cancellationToken).ConfigureAwait(false))
            {
                store.Remove(team);
            }
        }

        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<(Organization Organization, OrganizationMember Membership)> RequireMembershipAsync(
        Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var membership = await store.FindMemberAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);
        var organization = membership is { IsActive: true }
            ? await store.FindOrganizationAsync(organizationId, cancellationToken).ConfigureAwait(false)
            : null;
        return organization is null ? throw new ResourceNotFoundException("organization_not_found") : (organization, membership!);
    }

    internal static void RequirePermission(OrganizationMember membership, OrganizationPermission permission)
    {
        if (!membership.Can(permission))
        {
            throw new ForbiddenException();
        }
    }

    private async Task<OrganizationMember> RequireActiveMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        await store.FindMemberAsync(organizationId, userId, cancellationToken).ConfigureAwait(false) is { IsActive: true } member
            ? member
            : throw new ResourceNotFoundException("member_not_found");

    private async Task RequireAnotherOwnerAsync(Guid organizationId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var owners = (await store.ListActiveMembersAsync(organizationId, cancellationToken).ConfigureAwait(false))
            .Count(m => m.Role == OrganizationRole.Owner && m.UserId != ownerUserId);
        if (owners == 0)
        {
            throw new ConflictException("last_owner");
        }
    }

    private async Task<OrganizationInvitation> RequireOwnInvitationAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var invitation = await store.FindInvitationAsync(invitationId, cancellationToken).ConfigureAwait(false);
        if (account is not { EmailConfirmed: true } || invitation is null
            || invitation.NormalizedEmail != OrganizationInvitation.Normalize(account.Email)
            || !invitation.IsOpen(time.GetUtcNow()))
        {
            throw new ResourceNotFoundException("invitation_not_found");
        }

        return invitation;
    }

    private async Task SaveOrConflictAsync(CancellationToken cancellationToken, string code = "conflict")
    {
        if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException(code);
        }
    }
}
