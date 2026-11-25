using System.Text.Json;
using Audorix.KnockTwice.Api.Administration;
using Audorix.KnockTwice.Api.Identity;
using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Conversations;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Application.Installations;
using Audorix.KnockTwice.Application.Organizations;
using Audorix.KnockTwice.Application.PrivateChats;
using Audorix.KnockTwice.Application.Realtime;
using Audorix.KnockTwice.Application.Users;
using Audorix.KnockTwice.Contracts.V1.Admin;
using Audorix.KnockTwice.Domain.Authorization;
using Audorix.KnockTwice.Domain.Organizations;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Audorix.KnockTwice.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Api.Endpoints;

/// <summary>Body of a live-settings or provider change: setting key → new value (JSON null resets or removes).</summary>
internal sealed record AdminSettingsRequest(Dictionary<string, JsonElement>? Values);

/// <summary>
/// <c>/api/v1/admin</c> (Phase 9): installation configuration, users, organizations, roles and the
/// audit log. Each endpoint requires its own permission, checked against the account's current
/// roles in the database (a revoked role stops working on the next request). Nothing here exposes
/// conversations, messages or files.
/// </summary>
internal static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder v1)
    {
        var admin = v1.MapGroup("/admin").WithTags("Administration");

        admin.MapGet("/overview", GetOverviewAsync).RequireAuthorization(Permissions.InstallationRead)
            .WithSummary("Installation overview: counts, providers in use and pending restarts.");

        // ---- Live settings (database) and providers (settings file) ----
        admin.MapGet("/settings", (RuntimeSettingsManager settings) => TypedResults.Ok(settings.Describe().Select(ToResponse).ToList()))
            .RequireAuthorization(Permissions.InstallationRead)
            .WithSummary("Live settings: features, access, limits, languages and audit retention, with their source.");
        admin.MapPut("/settings", async (AdminSettingsRequest request, HttpContext http, RuntimeSettingsManager settings, CancellationToken ct) =>
            TypedResults.Ok((await settings.SaveAsync(http.User.RequireUserId(), request.Values, ct)).Select(ToResponse).ToList()))
            .RequireAuthorization(Permissions.InstallationManage)
            .WithSummary("Changes live settings; they apply to the next request. A null value resets a setting. Keys set by the environment are locked.");

        admin.MapGet("/providers", (ProviderSettingsManager providers) => TypedResults.Ok(providers.Describe().Select(ToResponse).ToList()))
            .RequireAuthorization(Permissions.InstallationRead)
            .WithSummary("Email, captcha, storage and scanning settings. Secret values are never returned.");
        admin.MapPut("/providers/{group}", async (string group, AdminSettingsRequest request, HttpContext http, ProviderSettingsManager providers, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await providers.SaveAsync(http.User.RequireUserId(), group, request.Values, ct))))
            .RequireAuthorization(Permissions.InstallationManage)
            .WithSummary("Validates and saves provider settings to the settings file; they apply after a restart. Omitted secrets stay unchanged.");
        admin.MapPost("/providers/{group}/test", async (
            string group, AdminSettingsRequest request, HttpContext http, ProviderSettingsManager providers, IUserAccountManager accounts, CancellationToken ct) =>
        {
            var me = await accounts.FindByIdAsync(http.User.RequireUserId(), ct) ?? throw new ResourceNotFoundException("user_not_found");
            return TypedResults.Ok(new ProviderTestResponse(await providers.TestAsync(me.Id, me.Email, group, request.Values, ct)));
        })
            .RequireAuthorization(Permissions.InstallationManage)
            .WithSummary("Tries provider settings without saving them: email sends to you, storage writes/reads/deletes a probe, scanning scans a sample.");

        // ---- Users and roles ----
        var users = admin.MapGroup("/users");
        users.MapGet(string.Empty, async (string? q, string? filter, string? role, string? sort, int? skip, int? take, UserAdministrationService service, CancellationToken ct) =>
        {
            var page = await service.SearchAsync(q, filter, skip ?? 0, take ?? 25, ct, role, sort);
            return TypedResults.Ok(new AdminUserPageResponse(page.Items.Select(ToResponse).ToList(), page.Total));
        }).RequireAuthorization(Permissions.UsersRead)
            .WithSummary("Searches accounts by email or name; filter: all, enabled, disabled, unconfirmed (or a role name); role: a role name or none; sort: nameAsc, nameDesc.");
        users.MapGet("/{userId:guid}", async (Guid userId, UserAdministrationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.GetAsync(userId, ct)))).RequireAuthorization(Permissions.UsersRead);
        users.MapPost("/{userId:guid}/disable", async (Guid userId, HttpContext http, UserAdministrationService service, IServiceProvider services, CancellationToken ct) =>
        {
            var result = await service.SetDisabledAsync(http.User.RequireUserId(), userId, true, ct);
            await DisconnectAsync(services, ct);
            return TypedResults.Ok(ToResponse(result));
        }).RequireAuthorization(Permissions.UsersManage).WithSummary("Disables an account and signs it out everywhere. Not possible for yourself or the last administrator.");
        users.MapDelete("/{userId:guid}", async (Guid userId, HttpContext http, AccountDeletionService deletion, IServiceProvider services, CancellationToken ct) =>
        {
            await deletion.DeleteByAdministratorAsync(http.User.RequireUserId(), userId, ct);
            await DisconnectAsync(services, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Permissions.UsersManage)
            .WithSummary("Deletes an account: personal data is erased, messages stay as \"Deleted user\". Refused for yourself, the last administrator and sole organization owners (409 sole_owner).");
        users.MapPost("/{userId:guid}/enable", async (Guid userId, HttpContext http, UserAdministrationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.SetDisabledAsync(http.User.RequireUserId(), userId, false, ct)))).RequireAuthorization(Permissions.UsersManage);
        users.MapPost("/{userId:guid}/revoke-sessions", async (Guid userId, HttpContext http, UserAdministrationService service, IServiceProvider services, CancellationToken ct) =>
        {
            var revoked = await service.RevokeSessionsAsync(http.User.RequireUserId(), userId, ct);
            await DisconnectAsync(services, ct);
            return TypedResults.Ok(new AdminSessionsRevokedResponse(revoked));
        }).RequireAuthorization(Permissions.UsersManage).WithSummary("Signs the account out on every device.");
        users.MapPost("/{userId:guid}/confirm-email", async (Guid userId, HttpContext http, UserAdministrationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.ConfirmEmailAsync(http.User.RequireUserId(), userId, ct)))).RequireAuthorization(Permissions.UsersManage);
        users.MapPost("/{userId:guid}/resend-confirmation", async (Guid userId, HttpContext http, UserAdministrationService service, CancellationToken ct) =>
        {
            await service.ResendConfirmationAsync(http.User.RequireUserId(), userId, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Permissions.UsersManage);
        users.MapPost("/{userId:guid}/roles", async (Guid userId, AdminRoleRequest request, HttpContext http, UserAdministrationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.GrantRoleAsync(http.User.RequireUserId(), userId, request.Role, ct))))
            .RequireAuthorization(Permissions.UsersManage).WithSummary("Grants an installation role.");
        users.MapDelete("/{userId:guid}/roles/{role}", async (Guid userId, string role, HttpContext http, UserAdministrationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.RevokeRoleAsync(http.User.RequireUserId(), userId, role, ct))))
            .RequireAuthorization(Permissions.UsersManage).WithSummary("Revokes an installation role (never the last administrator's).");

        admin.MapGet("/roles", async (UserAdministrationService service, CancellationToken ct) =>
        {
            var roles = new List<AdminRoleResponse>();
            foreach (var role in Roles.Active)
            {
                roles.Add(new AdminRoleResponse(role, Permissions.ForRole(role), (await service.SearchAsync(null, role, 0, 1, ct)).Total));
            }

            return TypedResults.Ok(roles);
        }).RequireAuthorization(Permissions.UsersRead).WithSummary("Installation roles and their permissions.");

        // ---- Organizations ----
        var organizations = admin.MapGroup("/organizations");
        organizations.MapGet(string.Empty, async (string? q, string? sort, int? skip, int? take, OrganizationAdministrationService service, CancellationToken ct) =>
        {
            var page = await service.SearchAsync(q, skip ?? 0, take ?? 25, ct, sort);
            return TypedResults.Ok(new AdminOrganizationPageResponse(page.Items.Select(ToResponse).ToList(), page.Total));
        }).RequireAuthorization(Permissions.OrganizationsRead)
            .WithSummary("Searches organizations by name; sort: nameAsc, nameDesc, members (most first).");
        organizations.MapGet("/{organizationId:guid}", async (Guid organizationId, OrganizationAdministrationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.GetAsync(organizationId, ct)))).RequireAuthorization(Permissions.OrganizationsRead);
        // The organization picture for the administration list: administrators are usually not members.
        organizations.MapGet("/{organizationId:guid}/photo", async (Guid organizationId, int? v, HttpContext http, OrganizationMediaService media, CancellationToken ct) =>
        {
            var image = await media.OpenImageForAdministratorAsync(organizationId, Domain.Organizations.OrganizationImage.Photo, ct);
            if (image is null)
            {
                return Results.NotFound();
            }

            AttachmentEndpoints.SetDownloadHeaders(http.Response, "photo", image.ContentType, forceDownload: false, cacheable: true);
            if (v == image.Version)
            {
                http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            }

            return Results.Stream(image.Content, image.ContentType);
        }).RequireAuthorization(Permissions.OrganizationsRead)
            .WithSummary("An organization's picture, for administrators (404 when it has none).");
        organizations.MapPut("/{organizationId:guid}", async (
            Guid organizationId, AdminRenameOrganizationRequest request, HttpContext http, OrganizationAdministrationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.RenameAsync(http.User.RequireUserId(), organizationId, request.Name, ct))))
            .RequireAuthorization(Permissions.OrganizationsManage);
        organizations.MapPost("/{organizationId:guid}/delete", async (
            Guid organizationId, Contracts.V1.Organizations.DeleteOrganizationRequest request, HttpContext http, OrganizationDeletionService service, CancellationToken ct) =>
        {
            await service.DeleteAsAdministratorAsync(http.User.RequireUserId(), organizationId, request.ConfirmName, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Permissions.OrganizationsManage)
            .WithSummary("Deletes an organization and everything in it (confirmName repeats its name). Cannot be undone.");
        organizations.MapPut("/{organizationId:guid}/members/{userId:guid}", async (
            Guid organizationId, Guid userId, AdminMemberRoleRequest request, HttpContext http, OrganizationAdministrationService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.ChangeMemberRoleAsync(http.User.RequireUserId(), organizationId, userId, ParseOrganizationRole(request.Role), ct))))
            .RequireAuthorization(Permissions.OrganizationsManage).WithSummary("Changes a member's organization role. The last owner cannot be demoted.");
        organizations.MapDelete("/{organizationId:guid}/members/{userId:guid}", async (
            Guid organizationId, Guid userId, HttpContext http, OrganizationAdministrationService service, ConversationService conversations, CancellationToken ct) =>
        {
            await service.RemoveMemberAsync(http.User.RequireUserId(), organizationId, userId, ct);
            await conversations.RemoveFromOrganizationAsync(http.User.RequireUserId(), organizationId, userId, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Permissions.OrganizationsManage).WithSummary("Removes a member (and their organization conversations). The last owner cannot be removed.");

        // ---- Audit log ----
        admin.MapGet("/audit", async (
            string? action, Guid? actor, string? target, bool? succeeded, DateTimeOffset? before, int? limit,
            DateTimeOffset? from, DateTimeOffset? to, string? q, int? skip, string? order,
            AuditService audit, IUserDirectory directory, CancellationToken ct) =>
        {
            var size = Math.Clamp(limit ?? 50, 1, 200);
            var oldestFirst = order?.Trim().ToLowerInvariant() switch
            {
                null or "" or "newest" => false,
                "oldest" => true,
                _ => throw new RequestValidationException("order", "order_invalid"),
            };
            var query = new AuditQuery(action, actor, target, succeeded, before?.ToUniversalTime(), size,
                from?.ToUniversalTime(), to?.ToUniversalTime(), q, Math.Max(0, skip ?? 0), oldestFirst);
            var entries = await audit.QueryAsync(query, ct);
            var names = await directory.GetSummariesAsync(entries.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!.Value).Distinct().ToList(), ct);
            var items = entries.Select(e => new AuditEntryResponse(
                e.Id, e.OccurredAtUtc, e.Action, e.Succeeded, e.ActorUserId, e.ActorUserId is { } id ? names.GetValueOrDefault(id)?.DisplayName : null,
                e.TargetType, e.TargetId, e.IpAddress, e.Details)).ToList();
            // Page numbers (skip given) come with a total; the older "before" cursor keeps working.
            var total = skip is not null ? await audit.CountAsync(query, ct) : (int?)null;
            return TypedResults.Ok(new AuditPageResponse(items, !oldestFirst && items.Count == size ? items[^1].OccurredAtUtc : null, total));
        }).RequireAuthorization(Permissions.AuditRead)
            .WithSummary("Audit log, newest first (order=oldest for oldest first). Filters: action (or prefix ending with '.'), actor, target, outcome, from/to, q (text in the action, target or actor name). With skip, the response has the total for page numbers.");

        return v1;
    }

    private static async Task<IResult> GetOverviewAsync(
        IServiceProvider services, InstallationService installations, IAdminDirectory directory, IHostEnvironment environment, PendingRestart pending,
        IOptions<DatabaseOptions> database, IOptions<StorageOptions> storage, IMalwareScanner scanner, IOptionsSnapshot<AccountSettings> accounts,
        CancellationToken ct)
    {
        var installation = await installations.FindAsync(ct);
        var users = await directory.SearchUsersAsync(null, null, 0, 1, ct);
        var disabled = await directory.SearchUsersAsync(null, "disabled", 0, 1, ct);
        var organizations = await directory.SearchOrganizationsAsync(null, 0, 1, ct);
        return TypedResults.Ok(new AdminOverviewResponse(
            installation?.Id,
            typeof(AdminEndpoints).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            environment.EnvironmentName,
            users.Total,
            disabled.Total,
            organizations.Total,
            database.Value.Provider ?? string.Empty,
            StorageOptions.Normalize(storage.Value.Provider),
            scanner.Enabled,
            accounts.Value.EffectiveRegistrationMode,
            ProviderSettingsManager.Groups.Select(g => g.Name).Where(pending.IsPending).ToList()));
    }

    /// <summary>Closes realtime connections of revoked sessions and ends private chats bound to them, right away.</summary>
    private static async Task DisconnectAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.GetRequiredService<PrivateChatService>().SweepAsync(cancellationToken).ConfigureAwait(false);
        await services.GetRequiredService<RealtimeService>().CloseRevokedSessionsAsync(cancellationToken).ConfigureAwait(false);
    }

    private static OrganizationRole ParseOrganizationRole(string? role) => role?.Trim().ToUpperInvariant() switch
    {
        "OWNER" => OrganizationRole.Owner,
        "ADMINISTRATOR" => OrganizationRole.Administrator,
        "MEMBER" => OrganizationRole.Member,
        _ => throw new RequestValidationException("role", "role_invalid"),
    };

    private static string RoleName(OrganizationRole role) => role switch
    {
        OrganizationRole.Owner => "owner",
        OrganizationRole.Administrator => "administrator",
        _ => "member",
    };

    private static AdminUserResponse ToResponse(AdminUserView u) =>
        new(u.Id, u.Email, u.DisplayName, u.Initials, u.EmailConfirmed, u.Disabled, u.LockedOut, u.Roles, u.CreatedAtUtc, u.LastSignInAtUtc, u.ActiveSessions, u.Organizations);

    private static AdminOrganizationResponse ToResponse(AdminOrganizationView o) => new(o.Id, o.Name, o.CreatedAtUtc, o.Members, o.Owners,
        o.PhotoVersion is { } version ? $"/api/v1/admin/organizations/{o.Id}/photo?v={version}" : null);

    private static AdminOrganizationDetailResponse ToResponse(AdminOrganizationDetail d) => new(
        ToResponse(d.Organization),
        d.Members.Select(m => new AdminOrganizationMemberResponse(m.User.Id, m.User.DisplayName, m.User.Initials, m.Email, RoleName(m.Role), m.JoinedAtUtc)).ToList());

    private static RuntimeSettingResponse ToResponse(RuntimeSettingView s) => new(s.Key, s.Group, s.Kind, s.Value, s.Source, s.Locked, s.Min, s.Max, s.Choices);

    private static ProviderGroupResponse ToResponse(ProviderGroupView g) =>
        new(g.Name, g.RestartRequired, g.Fields.Select(f => new ProviderFieldResponse(f.Key, f.Kind, f.Value, f.HasValue, f.Source, f.Locked, f.Choices)).ToList());
}
