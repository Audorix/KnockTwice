using Audorix.KnockTwice.Api.Identity;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Organizations;
using Audorix.KnockTwice.Application.Users;
using Audorix.KnockTwice.Contracts.V1.Organizations;
using Audorix.KnockTwice.Contracts.V1.Users;
using Audorix.KnockTwice.Contracts.V1.Admin;
using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Domain.Authorization;
using Audorix.KnockTwice.Domain.Organizations;
using Audorix.KnockTwice.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.DataProtection;

namespace Audorix.KnockTwice.Api.Endpoints;

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

        // Picture and background image (redesign 2026-11-24): {kind} is "photo" or "banner".
        orgs.MapPost("/{organizationId:guid}/{kind:regex(^(photo|banner)$)}", async (
            Guid organizationId, string kind, HttpContext http, OrganizationMediaService media, CancellationToken ct) =>
        {
            var image = ParseImage(kind);
            var view = await MultipartUpload.ReadFileAsync(http, OrganizationMediaService.MaxBytes(image),
                (_, _, stream) => media.SetImageAsync(http.User.RequireUserId(), organizationId, image, stream, ct), ct);
            return TypedResults.Ok(ToResponse(view));
        }).WithSummary("Sets the organization's picture (photo: square, up to 1 MB) or background image (banner: wide, up to 2 MB); multipart/form-data, field 'file', PNG, JPEG or WebP, already cropped. Owners and administrators.");
        orgs.MapDelete("/{organizationId:guid}/{kind:regex(^(photo|banner)$)}", async (
            Guid organizationId, string kind, HttpContext http, OrganizationMediaService media, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await media.RemoveImageAsync(http.User.RequireUserId(), organizationId, ParseImage(kind), ct))))
            .WithSummary("Removes the picture (initials show again) or the background image. Owners and administrators.");
        orgs.MapGet("/{organizationId:guid}/{kind:regex(^(photo|banner)$)}", async (
            Guid organizationId, string kind, int? v, HttpContext http, OrganizationMediaService media, CancellationToken ct) =>
        {
            var image = await media.OpenImageAsync(http.User.RequireUserId(), organizationId, ParseImage(kind), ct);
            if (image is null)
            {
                return Results.NotFound();
            }

            AttachmentEndpoints.SetDownloadHeaders(http.Response, kind, image.ContentType, forceDownload: false, cacheable: true);
            if (v == image.Version)
            {
                http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            }

            return Results.Stream(image.Content, image.ContentType);
        }).WithSummary("The organization's picture or background image (members). 404 when there is none.");

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
                ParseChannelCreation(request.ChannelCreation), request.PrivateChatsRequireSharedTeam, ct))))
            .WithSummary("Organization-specific configuration: guest access, channel creation, and whether organization-context private chats require a shared team (owners and administrators).");
        orgs.MapGet("/{organizationId:guid}/domains", async (
            Guid organizationId, HttpContext http, OrganizationDomainService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListAsync(http.User.RequireUserId(), organizationId, ct)).Select(ToResponse).ToList()))
            .WithSummary("Lists verified-domain ownership challenges (owners and administrators).");
        orgs.MapPost("/{organizationId:guid}/domains", async (
            Guid organizationId, AddOrganizationDomainRequest request, HttpContext http, OrganizationDomainService service, CancellationToken ct) =>
        {
            var domain = await service.AddAsync(http.User.RequireUserId(), organizationId, request.Domain, ct);
            return TypedResults.Created($"/api/v1/organizations/{organizationId}/domains/{domain.Id}", ToResponse(domain));
        }).WithSummary("Creates a DNS TXT ownership challenge. Creating a challenge does not enable automatic enrollment.");
        orgs.MapPost("/{organizationId:guid}/domains/{domainId:guid}/verify", async (
            Guid organizationId, Guid domainId, HttpContext http, OrganizationDomainService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.VerifyAsync(http.User.RequireUserId(), organizationId, domainId, ct))))
            .WithSummary("Verifies the ownership TXT record using the server's configured DNS resolvers.");
        orgs.MapDelete("/{organizationId:guid}/domains/{domainId:guid}", async (
            Guid organizationId, Guid domainId, HttpContext http, OrganizationDomainService service, CancellationToken ct) =>
        {
            await service.RemoveAsync(http.User.RequireUserId(), organizationId, domainId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Removes a domain ownership claim (owners and administrators).");
        orgs.MapGet("/{organizationId:guid}/domains/automatic-enrollment", async (
            Guid organizationId, HttpContext http, OrganizationDomainService service, CancellationToken ct) =>
            TypedResults.Ok(new AutomaticEnrollmentResponse(await service.GetAutomaticEnrollmentAsync(http.User.RequireUserId(), organizationId, ct))))
            .WithSummary("Reads whether confirmed accounts on a verified domain may join automatically (owners and administrators).");
        orgs.MapPut("/{organizationId:guid}/domains/automatic-enrollment", async (
            Guid organizationId, SetAutomaticEnrollmentRequest request, HttpContext http, OrganizationDomainService service, CancellationToken ct) =>
            TypedResults.Ok(new AutomaticEnrollmentResponse(await service.SetAutomaticEnrollmentAsync(http.User.RequireUserId(), organizationId, request.Enabled, ct))))
            .WithSummary("Opts into or out of automatic enrollment for confirmed email addresses matching a verified domain (owners and administrators; off by default).");
        orgs.MapGet("/{organizationId:guid}/sso", async (
            Guid organizationId, HttpContext http, OrganizationDomainService service, CancellationToken ct) =>
        {
            var organization = await service.GetSsoAsync(http.User.RequireUserId(), organizationId, ct);
            return TypedResults.Ok(new OrganizationSsoResponse(organization.SsoConfigured, organization.SsoIssuer,
                organization.SsoClientId, !string.IsNullOrEmpty(organization.ProtectedSsoClientSecret)));
        }).WithSummary("Reads organization OpenID Connect settings without returning the client secret.");
        orgs.MapPut("/{organizationId:guid}/sso", async (
            Guid organizationId, ConfigureOrganizationSsoRequest request, HttpContext http, OrganizationDomainService service,
            IDataProtectionProvider protection, OrganizationOidcSchemeManager ssoSchemes,
            Application.Administration.AuditService audit, CancellationToken ct) =>
        {
            if (request.ClientSecret?.Length > 2048)
                throw new DomainRuleException("organization_sso_secret_too_long", "The client secret is too long.");
            var actorId = http.User.RequireUserId();
            var current = await service.GetSsoAsync(actorId, organizationId, ct);
            var protectedSecret = string.IsNullOrWhiteSpace(request.ClientSecret)
                ? current.ProtectedSsoClientSecret
                : protection.CreateProtector("Audorix.KnockTwice.OrganizationSso.ClientSecret.v1").Protect(request.ClientSecret);
            var organization = await service.SetSsoAsync(actorId, organizationId, request.Issuer,
                request.ClientId, protectedSecret, ct);
            var signInSettings = await service.GetSsoForSignInAsync(organizationId, ct);
            if (signInSettings is not null)
            {
                var clientSecret = protection.CreateProtector("Audorix.KnockTwice.OrganizationSso.ClientSecret.v1")
                    .Unprotect(signInSettings.ProtectedClientSecret);
                await ssoSchemes.EnsureRegisteredAsync(signInSettings, clientSecret);
            }
            await audit.RecordAsync(Application.Administration.AuditActions.OrganizationSsoConfigured, actorId,
                "organization", organizationId.ToString(), cancellationToken: ct);
            return TypedResults.Ok(new OrganizationSsoResponse(organization.SsoConfigured, organization.SsoIssuer,
                organization.SsoClientId, !string.IsNullOrEmpty(organization.ProtectedSsoClientSecret)));
        }).WithSummary("Configures organization OpenID Connect sign-in (owners and administrators; secret is protected at rest).");
        orgs.MapDelete("/{organizationId:guid}/sso", async (
            Guid organizationId, HttpContext http, OrganizationDomainService service,
            OrganizationOidcSchemeManager ssoSchemes, Application.Administration.AuditService audit, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await service.ClearSsoAsync(actorId, organizationId, ct);
            ssoSchemes.Remove(organizationId);
            await audit.RecordAsync(Application.Administration.AuditActions.OrganizationSsoConfigurationRemoved, actorId,
                "organization", organizationId.ToString(), cancellationToken: ct);
            return TypedResults.NoContent();
        }).WithSummary("Removes saved organization OpenID Connect settings (owners and administrators).");
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

        var access = orgs.MapGroup("/{organizationId:guid}/access-control").WithTags("Organization access control");
        access.MapGet("/roles", async (Guid organizationId, HttpContext http, OrganizationService orgService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            await RequireAccessManagerAsync(http.User.RequireUserId(), organizationId, orgService, ct);
            return TypedResults.Ok((await service.ListOrganizationRolesAsync(organizationId, ct)).Select(ToAccessRoleResponse).ToList());
        });
        access.MapPost("/roles", async (Guid organizationId, CreateAccessControlRoleRequest request, HttpContext http, OrganizationService orgService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireAccessManagerAsync(actorId, organizationId, orgService, ct);
            var created = await service.CreateRoleAsync(actorId, request.Name, request.Description, ToPermissionAssignments(request.Permissions), ct,
                PermissionScopes.Organization, organizationId);
            return TypedResults.Created($"/api/v1/organizations/{organizationId}/access-control/roles/{created.Role.Id}", ToAccessRoleResponse(created));
        });
        access.MapPut("/roles/{roleId:guid}", async (Guid organizationId, Guid roleId, UpdateAccessControlRoleRequest request, HttpContext http, OrganizationService orgService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireAccessManagerAsync(actorId, organizationId, orgService, ct);
            return TypedResults.Ok(ToAccessRoleResponse(await service.UpdateRoleAsync(actorId, roleId, request.Name, request.Description,
                ToPermissionAssignments(request.Permissions), ct, PermissionScopes.Organization, organizationId)));
        });
        access.MapDelete("/roles/{roleId:guid}", async (Guid organizationId, Guid roleId, HttpContext http, OrganizationService orgService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireAccessManagerAsync(actorId, organizationId, orgService, ct);
            await service.DeleteRoleAsync(actorId, roleId, ct, PermissionScopes.Organization, organizationId);
            return TypedResults.NoContent();
        });
        access.MapGet("/groups", async (Guid organizationId, HttpContext http, OrganizationService orgService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            await RequireAccessManagerAsync(http.User.RequireUserId(), organizationId, orgService, ct);
            return TypedResults.Ok((await service.ListOrganizationGroupsAsync(organizationId, ct)).Select(ToAccessGroupResponse).ToList());
        });
        access.MapPost("/groups", async (Guid organizationId, CreateAccessControlGroupRequest request, HttpContext http, OrganizationService orgService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireAccessManagerAsync(actorId, organizationId, orgService, ct);
            var created = await service.CreateGroupAsync(actorId, request.Name, request.Description, ct, PermissionScopes.Organization, organizationId);
            return TypedResults.Created($"/api/v1/organizations/{organizationId}/access-control/groups/{created.Group.Id}", ToAccessGroupResponse(created));
        });
        access.MapPut("/groups/{groupId:guid}", async (Guid organizationId, Guid groupId, UpdateAccessControlGroupRequest request, HttpContext http, OrganizationService orgService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireAccessManagerAsync(actorId, organizationId, orgService, ct);
            return TypedResults.Ok(ToAccessGroupResponse(await service.UpdateGroupAsync(actorId, groupId, request.Name, request.Description, ct,
                PermissionScopes.Organization, organizationId)));
        });
        access.MapDelete("/groups/{groupId:guid}", async (Guid organizationId, Guid groupId, HttpContext http, OrganizationService orgService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireAccessManagerAsync(actorId, organizationId, orgService, ct);
            await service.DeleteGroupAsync(actorId, groupId, ct, PermissionScopes.Organization, organizationId);
            return TypedResults.NoContent();
        });
        access.MapPost("/roles/{roleId:guid}/groups/{groupId:guid}", async (Guid organizationId, Guid roleId, Guid groupId, HttpContext http, OrganizationService orgService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireAccessManagerAsync(actorId, organizationId, orgService, ct);
            return TypedResults.Ok(ToAccessGroupResponse(await service.AssignRoleToGroupAsync(actorId, groupId, roleId, ct, PermissionScopes.Organization, organizationId)));
        });
        access.MapDelete("/roles/{roleId:guid}/groups/{groupId:guid}", async (Guid organizationId, Guid roleId, Guid groupId, HttpContext http, OrganizationService orgService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireAccessManagerAsync(actorId, organizationId, orgService, ct);
            return TypedResults.Ok(ToAccessGroupResponse(await service.RemoveRoleFromGroupAsync(actorId, groupId, roleId, ct, PermissionScopes.Organization, organizationId)));
        });
        access.MapPost("/groups/{groupId:guid}/members/{userId:guid}", async (Guid organizationId, Guid groupId, Guid userId, HttpContext http, OrganizationService orgService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireAccessManagerAsync(actorId, organizationId, orgService, ct);
            return TypedResults.Ok(ToAccessGroupResponse(await service.AddMemberAsync(actorId, groupId, userId, ct, PermissionScopes.Organization, organizationId)));
        });
        access.MapDelete("/groups/{groupId:guid}/members/{userId:guid}", async (Guid organizationId, Guid groupId, Guid userId, HttpContext http, OrganizationService orgService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireAccessManagerAsync(actorId, organizationId, orgService, ct);
            return TypedResults.Ok(ToAccessGroupResponse(await service.RemoveMemberAsync(actorId, groupId, userId, ct, PermissionScopes.Organization, organizationId)));
        });

        MapWorkspaceAccessControl(orgs);

        return v1;
    }

    private static void MapWorkspaceAccessControl(RouteGroupBuilder orgs)
    {
        var access = orgs.MapGroup("/{organizationId:guid}/workspaces/{workspaceId:guid}/access-control").WithTags("Workspace access control");
        access.MapGet("/roles", async (Guid organizationId, Guid workspaceId, HttpContext http, OrganizationService orgService, WorkspaceService workspaceService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            await RequireWorkspaceAccessManagerAsync(http.User.RequireUserId(), organizationId, workspaceId, orgService, workspaceService, ct);
            return TypedResults.Ok((await service.ListWorkspaceRolesAsync(organizationId, workspaceId, ct)).Select(ToAccessRoleResponse).ToList());
        });
        access.MapPost("/roles", async (Guid organizationId, Guid workspaceId, CreateAccessControlRoleRequest request, HttpContext http, OrganizationService orgService, WorkspaceService workspaceService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireWorkspaceAccessManagerAsync(actorId, organizationId, workspaceId, orgService, workspaceService, ct);
            var created = await service.CreateRoleAsync(actorId, request.Name, request.Description, ToPermissionAssignments(request.Permissions), ct, PermissionScopes.Workspace, organizationId, workspaceId);
            return TypedResults.Created($"/api/v1/organizations/{organizationId}/workspaces/{workspaceId}/access-control/roles/{created.Role.Id}", ToAccessRoleResponse(created));
        });
        access.MapPut("/roles/{roleId:guid}", async (Guid organizationId, Guid workspaceId, Guid roleId, UpdateAccessControlRoleRequest request, HttpContext http, OrganizationService orgService, WorkspaceService workspaceService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireWorkspaceAccessManagerAsync(actorId, organizationId, workspaceId, orgService, workspaceService, ct);
            return TypedResults.Ok(ToAccessRoleResponse(await service.UpdateRoleAsync(actorId, roleId, request.Name, request.Description, ToPermissionAssignments(request.Permissions), ct, PermissionScopes.Workspace, organizationId, workspaceId)));
        });
        access.MapDelete("/roles/{roleId:guid}", async (Guid organizationId, Guid workspaceId, Guid roleId, HttpContext http, OrganizationService orgService, WorkspaceService workspaceService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireWorkspaceAccessManagerAsync(actorId, organizationId, workspaceId, orgService, workspaceService, ct);
            await service.DeleteRoleAsync(actorId, roleId, ct, PermissionScopes.Workspace, organizationId, workspaceId);
            return TypedResults.NoContent();
        });
        access.MapGet("/groups", async (Guid organizationId, Guid workspaceId, HttpContext http, OrganizationService orgService, WorkspaceService workspaceService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            await RequireWorkspaceAccessManagerAsync(http.User.RequireUserId(), organizationId, workspaceId, orgService, workspaceService, ct);
            return TypedResults.Ok((await service.ListWorkspaceGroupsAsync(organizationId, workspaceId, ct)).Select(ToAccessGroupResponse).ToList());
        });
        access.MapPost("/groups", async (Guid organizationId, Guid workspaceId, CreateAccessControlGroupRequest request, HttpContext http, OrganizationService orgService, WorkspaceService workspaceService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireWorkspaceAccessManagerAsync(actorId, organizationId, workspaceId, orgService, workspaceService, ct);
            var created = await service.CreateGroupAsync(actorId, request.Name, request.Description, ct, PermissionScopes.Workspace, organizationId, workspaceId);
            return TypedResults.Created($"/api/v1/organizations/{organizationId}/workspaces/{workspaceId}/access-control/groups/{created.Group.Id}", ToAccessGroupResponse(created));
        });
        access.MapPut("/groups/{groupId:guid}", async (Guid organizationId, Guid workspaceId, Guid groupId, UpdateAccessControlGroupRequest request, HttpContext http, OrganizationService orgService, WorkspaceService workspaceService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireWorkspaceAccessManagerAsync(actorId, organizationId, workspaceId, orgService, workspaceService, ct);
            return TypedResults.Ok(ToAccessGroupResponse(await service.UpdateGroupAsync(actorId, groupId, request.Name, request.Description, ct, PermissionScopes.Workspace, organizationId, workspaceId)));
        });
        access.MapDelete("/groups/{groupId:guid}", async (Guid organizationId, Guid workspaceId, Guid groupId, HttpContext http, OrganizationService orgService, WorkspaceService workspaceService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireWorkspaceAccessManagerAsync(actorId, organizationId, workspaceId, orgService, workspaceService, ct);
            await service.DeleteGroupAsync(actorId, groupId, ct, PermissionScopes.Workspace, organizationId, workspaceId);
            return TypedResults.NoContent();
        });
        access.MapPost("/roles/{roleId:guid}/groups/{groupId:guid}", async (Guid organizationId, Guid workspaceId, Guid roleId, Guid groupId, HttpContext http, OrganizationService orgService, WorkspaceService workspaceService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireWorkspaceAccessManagerAsync(actorId, organizationId, workspaceId, orgService, workspaceService, ct);
            return TypedResults.Ok(ToAccessGroupResponse(await service.AssignRoleToGroupAsync(actorId, groupId, roleId, ct, PermissionScopes.Workspace, organizationId, workspaceId)));
        });
        access.MapDelete("/roles/{roleId:guid}/groups/{groupId:guid}", async (Guid organizationId, Guid workspaceId, Guid roleId, Guid groupId, HttpContext http, OrganizationService orgService, WorkspaceService workspaceService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireWorkspaceAccessManagerAsync(actorId, organizationId, workspaceId, orgService, workspaceService, ct);
            return TypedResults.Ok(ToAccessGroupResponse(await service.RemoveRoleFromGroupAsync(actorId, groupId, roleId, ct, PermissionScopes.Workspace, organizationId, workspaceId)));
        });
        access.MapPost("/groups/{groupId:guid}/members/{userId:guid}", async (Guid organizationId, Guid workspaceId, Guid groupId, Guid userId, HttpContext http, OrganizationService orgService, WorkspaceService workspaceService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireWorkspaceAccessManagerAsync(actorId, organizationId, workspaceId, orgService, workspaceService, ct);
            return TypedResults.Ok(ToAccessGroupResponse(await service.AddMemberAsync(actorId, groupId, userId, ct, PermissionScopes.Workspace, organizationId, workspaceId)));
        });
        access.MapDelete("/groups/{groupId:guid}/members/{userId:guid}", async (Guid organizationId, Guid workspaceId, Guid groupId, Guid userId, HttpContext http, OrganizationService orgService, WorkspaceService workspaceService, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var actorId = http.User.RequireUserId();
            await RequireWorkspaceAccessManagerAsync(actorId, organizationId, workspaceId, orgService, workspaceService, ct);
            return TypedResults.Ok(ToAccessGroupResponse(await service.RemoveMemberAsync(actorId, groupId, userId, ct, PermissionScopes.Workspace, organizationId, workspaceId)));
        });
    }

    private static async Task RequireWorkspaceAccessManagerAsync(Guid userId, Guid organizationId, Guid workspaceId,
        OrganizationService organizations, WorkspaceService workspaces, CancellationToken ct)
    {
        await RequireAccessManagerAsync(userId, organizationId, organizations, ct);
        await workspaces.RequireWorkspaceAccessAsync(userId, organizationId, workspaceId, ct);
    }

    internal static UserSummaryResponse ToUser(UserSummary user) =>
        new(user.Id, user.DisplayName, new AvatarResponse(user.Initials, UserEndpoints.PhotoUrl(user.Id, user.PhotoVersion), user.GeneratedAvatarStyle, user.GeneratedAvatarSeed), user.Deleted);

    private static OrganizationResponse ToResponse(OrganizationView view) => new(
        view.Organization.Id,
        view.Organization.Name,
        RoleName(view.MyRole),
        view.Organization.CreatedAtUtc,
        view.MyPermissions ?? Enum.GetValues<OrganizationPermission>()
            .Where(p => OrganizationPermissions.IsGranted(view.MyRole, p))
            .Select(p => p switch
            {
                OrganizationPermission.Manage => "organization.manage",
                OrganizationPermission.ManageMembers => "organization.members.manage",
                OrganizationPermission.ManageRoles => "organization.roles.manage",
                _ => "organization.workspaces.manage",
            }).ToList(),
        view.IsSupportAgent,
        view.Organization.GuestsEnabled,
        view.Organization.ChannelCreation == ChannelCreationPolicy.Administrators ? "administrators" : "members",
        ImageUrl(view.Organization.Id, "photo", view.Organization.PhotoKey, view.Organization.PhotoVersion),
        ImageUrl(view.Organization.Id, "banner", view.Organization.BannerKey, view.Organization.BannerVersion),
        view.Organization.PrivateChatsRequireSharedTeam);

    /// <summary>Versioned, so a browser may cache it for good.</summary>
    private static string? ImageUrl(Guid organizationId, string kind, string? key, int version) =>
        key is null ? null : $"/api/v1/organizations/{organizationId}/{kind}?v={version}";

    private static OrganizationImage ParseImage(string kind) => kind switch
    {
        "photo" => OrganizationImage.Photo,
        "banner" => OrganizationImage.Banner,
        _ => throw new ResourceNotFoundException("not_found"),
    };

    private static ChannelCreationPolicy? ParseChannelCreation(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "members" => ChannelCreationPolicy.Members,
        "administrators" => ChannelCreationPolicy.Administrators,
        _ => throw new RequestValidationException("channelCreation", "value_invalid"),
    };

    private static InvitationResponse ToResponse(OrganizationInvitation invitation) =>
        new(invitation.Id, invitation.Email, RoleName(invitation.Role), invitation.CreatedAtUtc, invitation.ExpiresAtUtc);

    private static OrganizationDomainResponse ToResponse(VerifiedOrganizationDomain domain) =>
        new(domain.Id, domain.Domain, "_knocktwice-verification." + domain.Domain,
            "knocktwice-verification=" + domain.VerificationToken, domain.IsVerified, domain.CreatedAtUtc, domain.VerifiedAtUtc);

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

    private static async Task RequireAccessManagerAsync(Guid userId, Guid organizationId, OrganizationService organizations, CancellationToken ct)
    {
        await organizations.RequireRoleManagementAsync(userId, organizationId, ct);
    }

    private static List<RolePermissionDecision> ToPermissionAssignments(IReadOnlyList<AccessControlPermissionRequest>? permissions) =>
        (permissions ?? []).Select(permission => new RolePermissionDecision(permission.Key ?? string.Empty,
            Enum.TryParse<PermissionEffect>(permission.Effect, true, out var effect) ? effect : (PermissionEffect)0)).ToList();

    private static AccessControlRoleResponse ToAccessRoleResponse(AccessRoleView view) => new(view.Role.Id, view.Role.Name, view.Role.Description,
        view.Role.Scope, view.Permissions.Select(permission => new AccessControlPermissionResponse(permission.Key, permission.Effect.ToString().ToLowerInvariant())).ToList(),
        view.Groups, view.Role.OrganizationId, view.Role.WorkspaceId);

    private static AccessControlGroupResponse ToAccessGroupResponse(AccessGroupView view) => new(view.Group.Id, view.Group.Name, view.Group.Description,
        view.MemberIds, view.RoleIds, view.Group.Scope, view.Group.OrganizationId, view.Group.WorkspaceId);

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
