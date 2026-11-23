using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Organizations;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Contracts.V1.Organizations;
using Audorix.ChatX.Contracts.V1.Users;
using Audorix.ChatX.Domain.Organizations;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Audorix.ChatX.Api.Endpoints;

/// <summary>
/// <c>/api/v1/organizations</c>: organizations, memberships, invitations and workspaces. Every
/// route resolves the caller's membership server-side; ids from other organizations return 404.
/// </summary>
internal static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder v1)
    {
        var orgs = v1.MapGroup("/organizations").RequireAuthorization().WithTags("Organizations");
        orgs.MapGet(string.Empty, async (HttpContext http, OrganizationService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListMineAsync(http.User.RequireUserId(), ct)).Select(ToResponse).ToList()))
            .WithSummary("Organizations the caller is an active member of.");
        orgs.MapPost(string.Empty, async (CreateOrganizationRequest request, HttpContext http, OrganizationService service, CancellationToken ct) =>
        {
            var created = ToResponse(await service.CreateAsync(http.User.RequireUserId(), request.Name, ct));
            return TypedResults.Created($"/api/v1/organizations/{created.Id}", created);
        }).WithSummary("Creates an organization; the caller becomes its owner. A default workspace is created.");
        orgs.MapGet("/{organizationId:guid}", async (Guid organizationId, HttpContext http, OrganizationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.GetAsync(http.User.RequireUserId(), organizationId, ct))));
        orgs.MapPut("/{organizationId:guid}", async (Guid organizationId, UpdateOrganizationRequest request, HttpContext http, OrganizationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.RenameAsync(http.User.RequireUserId(), organizationId, request.Name, ct))))
            .WithSummary("Renames the organization (owners and administrators).");
        orgs.MapPost("/{organizationId:guid}/delete", async (
            Guid organizationId, DeleteOrganizationRequest request, HttpContext http, OrganizationDeletionService service, CancellationToken ct) =>
        {
            await service.DeleteAsOwnerAsync(http.User.RequireUserId(), organizationId, request.ConfirmName, ct);
            return TypedResults.NoContent();
        }).WithSummary("Deletes the organization and everything in it (owners only; confirmName repeats its name). Cannot be undone.");

        orgs.MapGet("/{organizationId:guid}/members", async (Guid organizationId, HttpContext http, OrganizationService service, CancellationToken ct) =>
        {
            var view = await service.ListMembersAsync(http.User.RequireUserId(), organizationId, ct);
            return TypedResults.Ok(new OrganizationMembersResponse(
                view.Members.Select(m => new OrganizationMemberResponse(ToUser(m.User), RoleName(m.Member.Role), m.Member.JoinedAtUtc, m.Member.IsSupportAgent)).ToList(),
                view.PendingInvitations?.Select(ToResponse).ToList()));
        }).WithSummary("Active members; pending invitations for owners and administrators.");
        orgs.MapPut("/{organizationId:guid}/members/{userId:guid}", async (
            Guid organizationId, Guid userId, ChangeMemberRoleRequest request, HttpContext http, OrganizationService service, CancellationToken ct) =>
        {
            var member = await service.ChangeRoleAsync(http.User.RequireUserId(), organizationId, userId, ParseOrganizationRole(request.Role), ct);
            return TypedResults.Ok(new OrganizationMemberResponse(ToUser(member.User), RoleName(member.Member.Role), member.Member.JoinedAtUtc, member.Member.IsSupportAgent));
        }).WithSummary("Changes a member's role (owners only). The last owner cannot be demoted.");
        orgs.MapPut("/{organizationId:guid}/members/{userId:guid}/support-agent", async (
            Guid organizationId, Guid userId, SetSupportAgentRequest request, HttpContext http, OrganizationService service, CancellationToken ct) =>
        {
            var member = await service.SetSupportAgentAsync(http.User.RequireUserId(), organizationId, userId, request.SupportAgent, ct);
            return TypedResults.Ok(new OrganizationMemberResponse(ToUser(member.User), RoleName(member.Member.Role), member.Member.JoinedAtUtc, member.Member.IsSupportAgent));
        }).WithSummary("Grants or removes the Support Agent role (owners and administrators).");
        orgs.MapPut("/{organizationId:guid}/settings", async (
            Guid organizationId, UpdateOrganizationSettingsRequest request, HttpContext http, OrganizationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.ConfigureAsync(http.User.RequireUserId(), organizationId, request.GuestsEnabled,
                ParseChannelCreation(request.ChannelCreation), ct))))
            .WithSummary("Organization-specific configuration: guests on/off and who creates channels (owners and administrators).");
        orgs.MapDelete("/{organizationId:guid}/members/{userId:guid}", async (
            Guid organizationId, Guid userId, HttpContext http, OrganizationService service,
            Application.Conversations.ConversationService conversations, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await service.RemoveMemberAsync(actorId, organizationId, userId, ct);
            // Leaving the organization also ends its conversations for that person (ADR-021/023).
            await conversations.RemoveFromOrganizationAsync(actorId, organizationId, userId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Removes a member, or leaves when the user is the caller. The last owner cannot leave. Ends the person's organization conversations.");

        orgs.MapPost("/{organizationId:guid}/invitations", async (
            Guid organizationId, CreateInvitationRequest request, HttpContext http, OrganizationService service, CancellationToken ct) =>
        {
            var invitation = await service.InviteAsync(http.User.RequireUserId(), organizationId, request.Email,
                request.Role is null ? OrganizationRole.Member : ParseOrganizationRole(request.Role), ct);
            return TypedResults.Created($"/api/v1/organizations/{organizationId}/invitations/{invitation.Id}", ToResponse(invitation));
        }).WithSummary("Invites an email address (owners and administrators). Does not reveal whether the address has an account.");
        orgs.MapDelete("/{organizationId:guid}/invitations/{invitationId:guid}", async (
            Guid organizationId, Guid invitationId, HttpContext http, OrganizationService service, CancellationToken ct) =>
        {
            await service.RevokeInvitationAsync(http.User.RequireUserId(), organizationId, invitationId, ct);
            return TypedResults.NoContent();
        });

        orgs.MapGet("/invitations", async (HttpContext http, OrganizationService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListMyInvitationsAsync(http.User.RequireUserId(), ct)).Select(i => new MyInvitationResponse(
                i.Invitation.Id, i.Invitation.OrganizationId, i.OrganizationName, RoleName(i.Invitation.Role),
                i.InvitedBy is null ? null : ToUser(i.InvitedBy), i.Invitation.ExpiresAtUtc)).ToList()))
            .WithSummary("Open invitations addressed to the caller's confirmed email address.");
        orgs.MapPost("/invitations/{invitationId:guid}/accept", async (Guid invitationId, HttpContext http, OrganizationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.AcceptInvitationAsync(http.User.RequireUserId(), invitationId, ct))));
        orgs.MapPost("/invitations/{invitationId:guid}/decline", async (Guid invitationId, HttpContext http, OrganizationService service, CancellationToken ct) =>
        {
            await service.DeclineInvitationAsync(http.User.RequireUserId(), invitationId, ct);
            return TypedResults.NoContent();
        });

        var workspaces = orgs.MapGroup("/{organizationId:guid}/workspaces").WithTags("Workspaces");
        workspaces.MapGet(string.Empty, async (Guid organizationId, HttpContext http, WorkspaceService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListAsync(http.User.RequireUserId(), organizationId, ct)).Select(ToResponse).ToList()))
            .WithSummary("Workspaces the caller can access (all of them for organization administrators).");
        workspaces.MapPost(string.Empty, async (Guid organizationId, CreateWorkspaceRequest request, HttpContext http, WorkspaceService service, CancellationToken ct) =>
        {
            var created = ToResponse(await service.CreateAsync(http.User.RequireUserId(), organizationId, request.Name, ct));
            return TypedResults.Created($"/api/v1/organizations/{organizationId}/workspaces/{created.Id}", created);
        }).WithSummary("Creates a workspace (organization owners and administrators).");
        workspaces.MapGet("/{workspaceId:guid}/members", async (
            Guid organizationId, Guid workspaceId, HttpContext http, WorkspaceService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListMembersAsync(http.User.RequireUserId(), organizationId, workspaceId, ct))
                .Select(m => new WorkspaceMemberResponse(ToUser(m.User), WorkspaceRoleName(m.Member.Role), m.Member.JoinedAtUtc)).ToList()));
        workspaces.MapPost("/{workspaceId:guid}/members", async (
            Guid organizationId, Guid workspaceId, AddWorkspaceMemberRequest request, HttpContext http, WorkspaceService service, CancellationToken ct) =>
        {
            var member = await service.AddMemberAsync(http.User.RequireUserId(), organizationId, workspaceId, request.UserId,
                request.Role is null ? WorkspaceRole.Member : ParseWorkspaceRole(request.Role), ct);
            return TypedResults.Ok(new WorkspaceMemberResponse(ToUser(member.User), WorkspaceRoleName(member.Member.Role), member.Member.JoinedAtUtc));
        }).WithSummary("Adds an active organization member to the workspace.");
        workspaces.MapDelete("/{workspaceId:guid}/members/{userId:guid}", async (
            Guid organizationId, Guid workspaceId, Guid userId, HttpContext http, WorkspaceService service,
            Application.Conversations.ConversationService conversations, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await service.RemoveMemberAsync(actorId, organizationId, workspaceId, userId, ct);
            // Leaving a workspace also ends its channels for that person (Phase 11).
            await conversations.RemoveFromWorkspaceChannelsAsync(actorId, workspaceId, userId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Removes a member, or leaves. Also ends the person's channels in the workspace and their team memberships.");
        workspaces.MapPut("/{workspaceId:guid}", async (
            Guid organizationId, Guid workspaceId, RenameWorkspaceRequest request, HttpContext http, WorkspaceService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.RenameAsync(http.User.RequireUserId(), organizationId, workspaceId, request.Name, ct))))
            .WithSummary("Renames a workspace (workspace or organization administrators).");
        workspaces.MapPut("/{workspaceId:guid}/members/{userId:guid}", async (
            Guid organizationId, Guid workspaceId, Guid userId, ChangeWorkspaceRoleRequest request, HttpContext http, WorkspaceService service, CancellationToken ct) =>
        {
            var member = await service.ChangeRoleAsync(http.User.RequireUserId(), organizationId, workspaceId, userId, ParseWorkspaceRole(request.Role ?? string.Empty), ct);
            return TypedResults.Ok(new WorkspaceMemberResponse(ToUser(member.User), WorkspaceRoleName(member.Member.Role), member.Member.JoinedAtUtc));
        }).WithSummary("Changes a workspace member's role (workspace or organization administrators).");

        return v1;
    }

    internal static UserSummaryResponse ToUser(UserSummary user) => new(user.Id, user.DisplayName, new AvatarResponse(user.Initials), user.Deleted);

    private static OrganizationResponse ToResponse(OrganizationView view) => new(
        view.Organization.Id,
        view.Organization.Name,
        RoleName(view.MyRole),
        view.Organization.CreatedAtUtc,
        Enum.GetValues<OrganizationPermission>()
            .Where(p => OrganizationPermissions.IsGranted(view.MyRole, p))
            .Select(p => p switch
            {
                OrganizationPermission.Manage => "organization.manage",
                OrganizationPermission.ManageMembers => "organization.members.manage",
                OrganizationPermission.ManageRoles => "organization.roles.manage",
                _ => "organization.workspaces.manage",
            })
            .ToList(),
        view.IsSupportAgent,
        view.Organization.GuestsEnabled,
        view.Organization.ChannelCreation == ChannelCreationPolicy.Administrators ? "administrators" : "members");

    private static ChannelCreationPolicy? ParseChannelCreation(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "members" => ChannelCreationPolicy.Members,
        "administrators" => ChannelCreationPolicy.Administrators,
        _ => throw new RequestValidationException("channelCreation", "value_invalid"),
    };

    private static InvitationResponse ToResponse(OrganizationInvitation invitation) =>
        new(invitation.Id, invitation.Email, RoleName(invitation.Role), invitation.CreatedAtUtc, invitation.ExpiresAtUtc);

    private static WorkspaceResponse ToResponse(WorkspaceView view) => new(
        view.Workspace.Id, view.Workspace.OrganizationId, view.Workspace.Name,
        view.MyRole is { } role ? WorkspaceRoleName(role) : null, view.Workspace.CreatedAtUtc);

    private static string RoleName(OrganizationRole role) => role switch
    {
        OrganizationRole.Owner => OrganizationRoles.Owner,
        OrganizationRole.Administrator => OrganizationRoles.Administrator,
        _ => OrganizationRoles.Member,
    };

    private static string WorkspaceRoleName(WorkspaceRole role) =>
        role == WorkspaceRole.Administrator ? WorkspaceRoles.Administrator : WorkspaceRoles.Member;

    private static OrganizationRole ParseOrganizationRole(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        OrganizationRoles.Owner => OrganizationRole.Owner,
        OrganizationRoles.Administrator => OrganizationRole.Administrator,
        OrganizationRoles.Member => OrganizationRole.Member,
        _ => throw new RequestValidationException("role", "role_invalid"),
    };

    private static WorkspaceRole ParseWorkspaceRole(string role) => role.Trim().ToLowerInvariant() switch
    {
        WorkspaceRoles.Administrator => WorkspaceRole.Administrator,
        WorkspaceRoles.Member => WorkspaceRole.Member,
        _ => throw new RequestValidationException("role", "role_invalid"),
    };
}
