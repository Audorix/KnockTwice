using System.Text.Json;
using System.Security.Cryptography;
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
using Audorix.KnockTwice.Domain.Attachments;
using Audorix.KnockTwice.Domain.Organizations;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Audorix.KnockTwice.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Api.Endpoints;

/// <summary>Body of a live-settings or provider change: setting key → new value (JSON null resets or removes).</summary>
internal sealed record AdminSettingsRequest(Dictionary<string, JsonElement>? Values);
internal sealed record StorageMigrationRollbackRequest(Guid ReverseCopyJobId);

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
            .RequireAuthorization("settings.read")
            .WithSummary("Live settings: features, access, limits, languages and audit retention, with their source.");
        admin.MapPut("/settings", async (AdminSettingsRequest request, HttpContext http, RuntimeSettingsManager settings, CancellationToken ct) =>
            TypedResults.Ok((await settings.SaveAsync(http.User.RequireUserId(), request.Values, ct)).Select(ToResponse).ToList()))
            .RequireAuthorization("settings.manage")
            .WithSummary("Changes live settings; they apply to the next request. A null value resets a setting. Keys set by the environment are locked.");

        admin.MapGet("/providers", (ProviderSettingsManager providers) => TypedResults.Ok(providers.Describe().Select(ToResponse).ToList()))
            .RequireAuthorization("providers.read")
            .WithSummary("Email, captcha, storage and scanning settings. Secret values are never returned.");
        admin.MapPut("/providers/{group}", async (string group, AdminSettingsRequest request, HttpContext http, ProviderSettingsManager providers, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await providers.SaveAsync(http.User.RequireUserId(), group, request.Values, ct))))
            .RequireAuthorization("providers.manage")
            .WithSummary("Validates and saves provider settings to the settings file; they apply after a restart. Omitted secrets stay unchanged.");
        admin.MapPost("/providers/{group}/test", async (
            string group, AdminSettingsRequest request, HttpContext http, ProviderSettingsManager providers, IUserAccountManager accounts, CancellationToken ct) =>
        {
            var me = await accounts.FindByIdAsync(http.User.RequireUserId(), ct) ?? throw new ResourceNotFoundException("user_not_found");
            return TypedResults.Ok(new ProviderTestResponse(await providers.TestAsync(me.Id, me.Email, group, request.Values, ct)));
        })
            .RequireAuthorization("providers.manage")
            .WithSummary("Tries provider settings without saving them: email sends to you, storage writes/reads/deletes a probe, scanning scans a sample.");

        var translationProviders = admin.MapGroup("/providers/ai/translation").WithTags("Administration");
        translationProviders.MapGet(string.Empty, (AiTranslationProviderProfiles profiles) => TypedResults.Ok(profiles.List()))
            .RequireAuthorization("providers.read")
            .WithSummary("Lists configured named AI translation providers with API keys masked.");
        translationProviders.MapPost(string.Empty, async (AiTranslationProviderProfileRequest request, HttpContext http, AiTranslationProviderProfiles profiles, CancellationToken ct) =>
        {
            var profile = await profiles.SaveAsync(http.User.RequireUserId(), null, request, ct).ConfigureAwait(false);
            return TypedResults.Created($"/api/v1/admin/providers/ai/translation/{profile.Id}", profile);
        }).RequireAuthorization("providers.manage");
        translationProviders.MapPut("/{profileId}", async (string profileId, AiTranslationProviderProfileRequest request, HttpContext http, AiTranslationProviderProfiles profiles, CancellationToken ct) =>
            TypedResults.Ok(await profiles.SaveAsync(http.User.RequireUserId(), profileId, request, ct).ConfigureAwait(false)))
            .RequireAuthorization("providers.manage");
        translationProviders.MapPost("/{profileId}/activate", async (string profileId, HttpContext http, AiTranslationProviderProfiles profiles, CancellationToken ct) =>
        {
            await profiles.ActivateAsync(http.User.RequireUserId(), profileId, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).RequireAuthorization("providers.manage");
        translationProviders.MapDelete("/{profileId}", async (string profileId, HttpContext http, AiTranslationProviderProfiles profiles, CancellationToken ct) =>
        {
            await profiles.DeleteAsync(http.User.RequireUserId(), profileId, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).RequireAuthorization("providers.manage");
        translationProviders.MapPost("/{profileId}/test", async (string profileId, HttpContext http, AiTranslationProviderProfiles profiles, CancellationToken ct) =>
            TypedResults.Ok(new ProviderTestResponse(await profiles.TestAsync(http.User.RequireUserId(), profileId, ct).ConfigureAwait(false))))
            .RequireAuthorization("providers.manage");

        admin.MapPost("/storage-migrations/dry-run", async (StorageMigrationDryRunRequest request, StorageMigrationService migrations, CancellationToken ct) =>
        {
            if (string.Equals(request.SourceProvider?.Trim(), request.DestinationProvider?.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new RequestValidationException("destinationProvider", "providers_must_differ");
            return TypedResults.Ok(await migrations.DryRunAsync(request.SourceProvider, request.DestinationProvider, ct).ConfigureAwait(false));
        })
            .RequireAuthorization("providers.manage")
            .WithSummary("Checks attachment inventory, source readability, and destination access without copying files or changing storage configuration.");

        admin.MapPost("/storage-migrations", async (StorageMigrationDryRunRequest request, StorageMigrationService migrations,
            ChatXDbContext db, IAttachmentStore attachmentStore, IAttachmentStorageProviderRegistry providers, CancellationToken ct) =>
        {
            var report = await migrations.DryRunAsync(request.SourceProvider, request.DestinationProvider, ct).ConfigureAwait(false);
            if (report.Blockers.Count > 0) throw new RequestValidationException("storageMigration", "preflight_blocked");
            var id = Guid.CreateVersion7();
            var now = DateTimeOffset.UtcNow;
            var job = new AttachmentStorageMigrationJob { Id = id, SourceProvider = report.SourceProvider, DestinationProvider = report.DestinationProvider, State = "Queued", CreatedAtUtc = now, UpdatedAtUtc = now };
            db.AttachmentStorageMigrationJobs.Add(job);
            const int pageSize = 250;
            var skip = 0;
            while (true)
            {
                var files = await attachmentStore.ListStorageMigrationCandidatesAsync(report.SourceProvider, providers.DefaultProvider, skip, pageSize, ct).ConfigureAwait(false);
                if (files.Count == 0) break;
                db.AttachmentStorageMigrationFiles.AddRange(files.Select(a => new AttachmentStorageMigrationFile
                {
                    Id = Guid.CreateVersion7(), JobId = id, AttachmentId = a.Id, StorageKey = a.StorageKey, FileName = a.FileName,
                    ContentType = a.ContentType, Size = a.Size, Sha256 = a.Sha256, State = "Pending", AvailableAtTicks = 0,
                }));
                skip += files.Count;
                if (files.Count < pageSize) break;
            }
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            var total = await db.AttachmentStorageMigrationFiles.LongCountAsync(x => x.JobId == id, ct).ConfigureAwait(false);
            return TypedResults.Accepted($"/api/v1/admin/storage-migrations/{id}", new { id, sourceProvider = job.SourceProvider, destinationProvider = job.DestinationProvider, state = job.State, totalFiles = total, copiedFiles = 0, failedFiles = 0, skippedFiles = 0, createdAtUtc = now, updatedAtUtc = now });
        }).RequireAuthorization("providers.manage")
            .WithSummary("Creates a durable attachment copy job after a successful preflight. Does not change attachment references or the active provider.");

        admin.MapGet("/storage-migrations", async (ChatXDbContext db, CancellationToken ct) =>
        {
            var jobs = await db.AttachmentStorageMigrationJobs.AsNoTracking().OrderByDescending(x => x.Id).Take(50).ToListAsync(ct).ConfigureAwait(false);
            var result = new List<object>();
            foreach (var job in jobs) result.Add(await StorageMigrationJobResponseAsync(db, job, ct).ConfigureAwait(false));
            return TypedResults.Ok(result);
        }).RequireAuthorization("providers.manage");

        admin.MapGet("/storage-migrations/{jobId:guid}", async (Guid jobId, ChatXDbContext db, CancellationToken ct) =>
        {
            var job = await db.AttachmentStorageMigrationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId, ct).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("storage_migration_not_found");
            return TypedResults.Ok(await StorageMigrationJobResponseAsync(db, job, ct).ConfigureAwait(false));
        }).RequireAuthorization("providers.manage");

        admin.MapPost("/storage-migrations/{jobId:guid}/cutover", async (Guid jobId, HttpContext http, ChatXDbContext db,
            IAttachmentStorageProviderRegistry providers, ProviderSettingsManager settings, AuditService audit, CancellationToken ct) =>
        {
            var job = await db.AttachmentStorageMigrationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId, ct).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("storage_migration_not_found");
            if (job.State != "Completed" || providers.DefaultProvider != job.SourceProvider)
                throw new ConflictException("storage_migration_not_ready_for_cutover");
            var locked = await db.AttachmentStorageMigrationJobs.Where(x => x.Id == jobId && x.State == "Completed")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "CuttingOver").SetProperty(x => x.UpdatedAtUtc, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
            if (locked != 1) throw new ConflictException("storage_migration_not_ready_for_cutover");
            var providerSettingSaved = false;
            try
            {
                if (!await VerifyMigrationInventoryAsync(db, job.Id, job.SourceProvider, job.DestinationProvider, providers, ct).ConfigureAwait(false))
                    throw new ConflictException("storage_migration_reconciliation_required");
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var referencesMoved = await MoveProviderReferencesAsync(db, job.SourceProvider, job.DestinationProvider, providers.DefaultProvider, ct).ConfigureAwait(false);
                await SaveStorageProviderAsync(settings, http.User.RequireUserId(), job.DestinationProvider, ct).ConfigureAwait(false);
                providerSettingSaved = true;
                await db.AttachmentStorageMigrationJobs.Where(x => x.Id == jobId && x.State == "CuttingOver")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "AwaitingRestart").SetProperty(x => x.UpdatedAtUtc, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
                await audit.RecordAsync(AuditActions.StorageMigrationCutover, http.User.RequireUserId(), "storage_migration", jobId.ToString("D"),
                    new Dictionary<string, object?> { ["sourceProvider"] = job.SourceProvider, ["destinationProvider"] = job.DestinationProvider, ["referencesMoved"] = referencesMoved },
                    cancellationToken: ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return TypedResults.Ok(new { jobId, state = "AwaitingRestart", activeProvider = job.DestinationProvider, referencesMoved, restartRequired = true });
            }
            catch
            {
                if (!providerSettingSaved)
                    await db.AttachmentStorageMigrationJobs.Where(x => x.Id == jobId && x.State == "CuttingOver")
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Completed").SetProperty(x => x.UpdatedAtUtc, DateTimeOffset.UtcNow), CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }).RequireAuthorization("providers.manage")
            .WithSummary("Verifies the copied inventory, freezes new uploads, moves attachment references, and changes the default provider. Requires an application restart to complete the switch.");

        admin.MapPost("/storage-migrations/{jobId:guid}/rollback", async (Guid jobId, StorageMigrationRollbackRequest request, HttpContext http,
            ChatXDbContext db, IAttachmentStorageProviderRegistry providers, ProviderSettingsManager settings, AuditService audit, CancellationToken ct) =>
        {
            var job = await db.AttachmentStorageMigrationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId, ct).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("storage_migration_not_found");
            var reverse = await db.AttachmentStorageMigrationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ReverseCopyJobId, ct).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("storage_migration_not_found");
            if (job.State != "CutoverComplete" || providers.DefaultProvider != job.DestinationProvider
                || reverse.State != "Completed" || reverse.SourceProvider != job.DestinationProvider || reverse.DestinationProvider != job.SourceProvider)
                throw new ConflictException("storage_migration_not_ready_for_rollback");
            var locked = await db.AttachmentStorageMigrationJobs.Where(x => x.Id == jobId && x.State == "CutoverComplete")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "RollbackCuttingOver").SetProperty(x => x.UpdatedAtUtc, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
            if (locked != 1) throw new ConflictException("storage_migration_not_ready_for_rollback");
            var providerSettingSaved = false;
            try
            {
                if (!await VerifyMigrationInventoryAsync(db, reverse.Id, reverse.SourceProvider, reverse.DestinationProvider, providers, ct).ConfigureAwait(false))
                    throw new ConflictException("storage_migration_reconciliation_required");
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var referencesMoved = await MoveProviderReferencesAsync(db, job.DestinationProvider, job.SourceProvider, providers.DefaultProvider, ct).ConfigureAwait(false);
                await SaveStorageProviderAsync(settings, http.User.RequireUserId(), job.SourceProvider, ct).ConfigureAwait(false);
                providerSettingSaved = true;
                await db.AttachmentStorageMigrationJobs.Where(x => x.Id == jobId && x.State == "RollbackCuttingOver")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "RollbackAwaitingRestart").SetProperty(x => x.UpdatedAtUtc, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
                await audit.RecordAsync(AuditActions.StorageMigrationRollback, http.User.RequireUserId(), "storage_migration", jobId.ToString("D"),
                    new Dictionary<string, object?> { ["reverseCopyJobId"] = request.ReverseCopyJobId, ["restoredProvider"] = job.SourceProvider, ["referencesMoved"] = referencesMoved },
                    cancellationToken: ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return TypedResults.Ok(new { jobId, state = "RollbackAwaitingRestart", activeProvider = job.SourceProvider, referencesMoved, restartRequired = true });
            }
            catch
            {
                if (!providerSettingSaved)
                    await db.AttachmentStorageMigrationJobs.Where(x => x.Id == jobId && x.State == "RollbackCuttingOver")
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "CutoverComplete").SetProperty(x => x.UpdatedAtUtc, DateTimeOffset.UtcNow), CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }).RequireAuthorization("providers.manage")
            .WithSummary("Uses a completed reverse copy job to verify and restore the previous provider. New uploads remain paused until the application restarts.");

        admin.MapPost("/storage-migrations/{jobId:guid}/pause", async (Guid jobId, ChatXDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var updated = await db.AttachmentStorageMigrationJobs.Where(x => x.Id == jobId && (x.State == "Queued" || x.State == "Running"))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Paused").SetProperty(x => x.UpdatedAtUtc, now), ct).ConfigureAwait(false);
            if (updated == 0 && !await db.AttachmentStorageMigrationJobs.AnyAsync(x => x.Id == jobId, ct).ConfigureAwait(false)) throw new ResourceNotFoundException("storage_migration_not_found");
            var job = await db.AttachmentStorageMigrationJobs.AsNoTracking().SingleAsync(x => x.Id == jobId, ct).ConfigureAwait(false);
            return TypedResults.Ok(await StorageMigrationJobResponseAsync(db, job, ct).ConfigureAwait(false));
        }).RequireAuthorization("providers.manage");

        admin.MapPost("/storage-migrations/{jobId:guid}/resume", async (Guid jobId, ChatXDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var updated = await db.AttachmentStorageMigrationJobs.Where(x => x.Id == jobId && x.State == "Paused")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Queued").SetProperty(x => x.UpdatedAtUtc, now)
                    .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseExpiresAtTicks, (long?)null), ct).ConfigureAwait(false);
            if (updated == 0 && !await db.AttachmentStorageMigrationJobs.AnyAsync(x => x.Id == jobId, ct).ConfigureAwait(false)) throw new ResourceNotFoundException("storage_migration_not_found");
            var job = await db.AttachmentStorageMigrationJobs.AsNoTracking().SingleAsync(x => x.Id == jobId, ct).ConfigureAwait(false);
            return TypedResults.Ok(await StorageMigrationJobResponseAsync(db, job, ct).ConfigureAwait(false));
        }).RequireAuthorization("providers.manage");

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
        }).RequireAuthorization("roles.read").WithSummary("Installation roles and their permissions.");

        admin.MapGet("/permissions/catalog", () => TypedResults.Ok(PermissionCatalog.All
            .Select(permission => new AdminPermissionDefinitionResponse(permission.Key, permission.Area, permission.Scope, permission.State, permission.Description))
            .ToList()))
            .RequireAuthorization("permissions.read")
            .WithSummary("Stable permission keys, evaluation scope and implementation state.");

        // ---- Custom installation roles, groups and effective access ----
        var access = admin.MapGroup("/access-control").WithTags("Administration");
        access.MapGet("/roles", async (ScopedAccessControlService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListRolesAsync(ct).ConfigureAwait(false)).Select(ToResponse).ToList()))
            .RequireAuthorization("roles.read");
        access.MapPost("/roles", async (CreateAccessControlRoleRequest request, HttpContext http, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var role = await service.CreateRoleAsync(http.User.RequireUserId(), request.Name, request.Description, ToPermissionAssignments(request.Permissions), ct).ConfigureAwait(false);
            return TypedResults.Created($"/api/v1/admin/access-control/roles/{role.Role.Id}", ToResponse(role));
        }).RequireAuthorization("roles.manage").RequireAuthorization("permissions.manage").RequireAuthorization("permissions.read");
        access.MapPut("/roles/{roleId:guid}", async (Guid roleId, UpdateAccessControlRoleRequest request, HttpContext http, ScopedAccessControlService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.UpdateRoleAsync(http.User.RequireUserId(), roleId, request.Name, request.Description, ToPermissionAssignments(request.Permissions), ct).ConfigureAwait(false))))
            .RequireAuthorization("roles.manage").RequireAuthorization("permissions.manage").RequireAuthorization("permissions.read");
        access.MapDelete("/roles/{roleId:guid}", async (Guid roleId, HttpContext http, ScopedAccessControlService service, CancellationToken ct) =>
        {
            await service.DeleteRoleAsync(http.User.RequireUserId(), roleId, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).RequireAuthorization("roles.manage");
        access.MapPost("/roles/{roleId:guid}/groups/{groupId:guid}", async (Guid roleId, Guid groupId, HttpContext http, ScopedAccessControlService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.AssignRoleToGroupAsync(http.User.RequireUserId(), groupId, roleId, ct).ConfigureAwait(false))))
            .RequireAuthorization("roles.manage").RequireAuthorization("groups.manage");
        access.MapDelete("/roles/{roleId:guid}/groups/{groupId:guid}", async (Guid roleId, Guid groupId, HttpContext http, ScopedAccessControlService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.RemoveRoleFromGroupAsync(http.User.RequireUserId(), groupId, roleId, ct).ConfigureAwait(false))))
            .RequireAuthorization("roles.manage").RequireAuthorization("groups.manage");

        access.MapGet("/groups", async (ScopedAccessControlService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListGroupsAsync(ct).ConfigureAwait(false)).Select(ToResponse).ToList()))
            .RequireAuthorization("groups.read");
        access.MapPost("/groups", async (CreateAccessControlGroupRequest request, HttpContext http, ScopedAccessControlService service, CancellationToken ct) =>
        {
            var group = await service.CreateGroupAsync(http.User.RequireUserId(), request.Name, request.Description, ct).ConfigureAwait(false);
            return TypedResults.Created($"/api/v1/admin/access-control/groups/{group.Group.Id}", ToResponse(group));
        }).RequireAuthorization("groups.manage");
        access.MapPut("/groups/{groupId:guid}", async (Guid groupId, UpdateAccessControlGroupRequest request, HttpContext http, ScopedAccessControlService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.UpdateGroupAsync(http.User.RequireUserId(), groupId, request.Name, request.Description, ct).ConfigureAwait(false))))
            .RequireAuthorization("groups.manage");
        access.MapDelete("/groups/{groupId:guid}", async (Guid groupId, HttpContext http, ScopedAccessControlService service, CancellationToken ct) =>
        {
            await service.DeleteGroupAsync(http.User.RequireUserId(), groupId, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).RequireAuthorization("groups.manage");
        access.MapPost("/groups/{groupId:guid}/members/{userId:guid}", async (Guid groupId, Guid userId, HttpContext http, ScopedAccessControlService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.AddMemberAsync(http.User.RequireUserId(), groupId, userId, ct).ConfigureAwait(false))))
            .RequireAuthorization("groups.manage");
        access.MapDelete("/groups/{groupId:guid}/members/{userId:guid}", async (Guid groupId, Guid userId, HttpContext http, ScopedAccessControlService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.RemoveMemberAsync(http.User.RequireUserId(), groupId, userId, ct).ConfigureAwait(false))))
            .RequireAuthorization("groups.manage");

        users.MapGet("/{userId:guid}/effective-permissions", async (Guid userId, ScopedAccessControlService service, IUserAccountManager accounts, CancellationToken ct) =>
        {
            var account = await accounts.FindByIdAsync(userId, ct).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("user_not_found");
            var custom = await service.GetEffectivePermissionsAsync(userId, ct).ConfigureAwait(false);
            var granted = Permissions.All.Where(permission => Permissions.IsEffectivelyGranted(
                    account.Roles, permission, custom.Allowed, custom.Denied, custom.Configured ?? []))
                .Order(StringComparer.Ordinal).ToList();
            return TypedResults.Ok(new EffectivePermissionsResponse(userId, granted, custom.Denied));
        })
            .RequireAuthorization("permissions.read");

        users.MapGet("/{userId:guid}/access-review-contexts", async (Guid userId, IUserAccountManager accounts, IOrganizationStore organizationStore, CancellationToken ct) =>
        {
            if (await accounts.FindByIdAsync(userId, ct).ConfigureAwait(false) is null) throw new ResourceNotFoundException("user_not_found");
            var contexts = new List<EffectiveAccessReviewContextResponse> { new(PermissionScopes.Installation, null, null, "Installation") };
            foreach (var (organization, _) in await organizationStore.ListActiveMembershipsAsync(userId, ct).ConfigureAwait(false))
            {
                contexts.Add(new EffectiveAccessReviewContextResponse(PermissionScopes.Organization, organization.Id, null, organization.Name));
                var workspaces = await organizationStore.ListWorkspacesAsync(organization.Id, ct).ConfigureAwait(false);
                var memberships = await organizationStore.ListWorkspaceMembershipsAsync(organization.Id, userId, ct).ConfigureAwait(false);
                foreach (var member in memberships.Where(item => item.IsActive))
                {
                    var workspace = workspaces.FirstOrDefault(item => item.Id == member.WorkspaceId);
                    if (workspace is not null) contexts.Add(new EffectiveAccessReviewContextResponse(PermissionScopes.Workspace, organization.Id, workspace.Id, $"{organization.Name} / {workspace.Name}"));
                }
            }
            return TypedResults.Ok(contexts);
        }).RequireAuthorization("permissions.read").WithSummary("Installation, organization, and workspace contexts available for reviewing this user's effective access.");

        users.MapGet("/{userId:guid}/access-review", async (Guid userId, string? scope, Guid? organizationId, Guid? workspaceId, ScopedAccessControlService service, IUserAccountManager accounts, IOrganizationStore organizationStore, CancellationToken ct) =>
        {
            var account = await accounts.FindByIdAsync(userId, ct).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("user_not_found");
            scope = string.IsNullOrWhiteSpace(scope) ? PermissionScopes.Installation : scope.Trim().ToLowerInvariant();
            if (scope is not (PermissionScopes.Installation or PermissionScopes.Organization or PermissionScopes.Workspace))
                throw new RequestValidationException("scope", "scope_invalid");
            OrganizationMember? orgMember = null;
            WorkspaceMember? workspaceMember = null;
            if (scope != PermissionScopes.Installation)
            {
                if (organizationId is null) throw new RequestValidationException("organizationId", "organization_required");
                orgMember = await organizationStore.FindMemberAsync(organizationId.Value, userId, ct).ConfigureAwait(false);
                if (orgMember is not { IsActive: true }) throw new ResourceNotFoundException("member_not_found");
            }
            if (scope == PermissionScopes.Workspace)
            {
                if (workspaceId is null) throw new RequestValidationException("workspaceId", "workspace_required");
                workspaceMember = await organizationStore.FindWorkspaceMemberAsync(workspaceId.Value, userId, ct).ConfigureAwait(false);
                if (workspaceMember is not { IsActive: true } || workspaceMember.OrganizationId != organizationId) throw new ResourceNotFoundException("workspace_member_not_found");
            }
            else if (workspaceId is not null) throw new RequestValidationException("workspaceId", "workspace_unexpected");

            var groups = new List<AccessGroupView>(await service.ListGroupsAsync(ct).ConfigureAwait(false));
            var roles = new List<AccessRoleView>(await service.ListRolesAsync(ct).ConfigureAwait(false));
            if (scope is PermissionScopes.Organization or PermissionScopes.Workspace)
            {
                groups.AddRange(await service.ListOrganizationGroupsAsync(organizationId!.Value, ct).ConfigureAwait(false));
                roles.AddRange(await service.ListOrganizationRolesAsync(organizationId.Value, ct).ConfigureAwait(false));
            }
            if (scope == PermissionScopes.Workspace)
            {
                groups.AddRange(await service.ListWorkspaceGroupsAsync(organizationId!.Value, workspaceId!.Value, ct).ConfigureAwait(false));
                roles.AddRange(await service.ListWorkspaceRolesAsync(organizationId.Value, workspaceId.Value, ct).ConfigureAwait(false));
            }
            groups = groups.Where(group => group.MemberIds.Contains(userId)).ToList();
            var roleById = roles.ToDictionary(item => item.Role.Id);
            var paths = groups.Select(group => new EffectiveAccessGroupResponse(group.Group.Id, group.Group.Name, group.Group.Scope,
                group.RoleIds.Where(roleById.ContainsKey).Select(roleId => roleById[roleId].Role.Name).Order(StringComparer.Ordinal).ToList())).ToList();
            var activeDecisions = groups.SelectMany(group => group.RoleIds.Where(roleById.ContainsKey)
                    .SelectMany(roleId => roleById[roleId].Permissions.Select(permission => new
                    {
                        Group = group.Group.Name,
                        Scope = group.Group.Scope,
                        Role = roleById[roleId].Role.Name,
                        permission.Key,
                        permission.Effect
                    })))
                .ToList();
            var configured = roles.Where(role => role.Groups > 0).SelectMany(role => role.Permissions)
                .Select(permission => permission.Key).ToHashSet(StringComparer.Ordinal);
            var reviewScopes = scope switch
            {
                PermissionScopes.Installation => new[] { PermissionScopes.Installation },
                PermissionScopes.Organization => new[] { PermissionScopes.Installation, PermissionScopes.Organization },
                _ => new[] { PermissionScopes.Installation, PermissionScopes.Organization, PermissionScopes.Workspace }
            };
            var permissionKeys = PermissionCatalog.All.Where(item => reviewScopes.Contains(item.Scope, StringComparer.Ordinal) && item.State == PermissionCatalog.Implemented)
                .Select(item => item.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            var permissionRows = permissionKeys.Select(key =>
            {
                var decisions = activeDecisions.Where(item => item.Key == key).ToList();
                var deny = decisions.Any(item => item.Effect == PermissionEffect.Deny);
                var allow = decisions.Any(item => item.Effect == PermissionEffect.Allow);
                var customConfigured = configured.Contains(key);
                var definition = PermissionCatalog.Find(key);
                var legacyGrant = definition?.Scope == PermissionScopes.Installation
                    ? Permissions.IsGrantedBy(account.Roles, key) || (definition?.LegacyFallbackKey is { } legacyFallback && Permissions.IsGrantedBy(account.Roles, legacyFallback))
                    : GetLegacyScopedGrant(key, orgMember, workspaceMember);
                var effective = definition?.Scope == PermissionScopes.Installation
                    ? Permissions.IsEffectivelyGranted(account.Roles, key, deny ? [] : allow ? [key] : [], deny ? [key] : [], customConfigured ? [key] : [])
                    : deny ? false : allow ? true : customConfigured ? false : legacyGrant;
                var source = deny ? "group_deny" : allow ? "group_allow" : legacyGrant ? "legacy_policy" : customConfigured ? "configured_no_grant" : "default_denied";
                var outcome = deny ? "denied" : effective ? "allowed" : "not_granted";
                var detail = decisions.Select(item => $"{item.Scope} / {item.Group} / {item.Role}: {item.Effect.ToString().ToLowerInvariant()}")
                    .Order(StringComparer.Ordinal).ToList();
                if (detail.Count == 0 && scope == PermissionScopes.Installation && definition?.LegacyFallbackKey is { } fallback)
                    detail.Add($"Legacy policy ({fallback}): {(effective ? "allow" : "deny")}");
                else if (detail.Count == 0 && source == "legacy_policy")
                    detail.Add("Existing scope policy applies.");
                return new EffectiveAccessPermissionResponse(key, outcome, source, detail);
            }).ToList();
            return TypedResults.Ok(new EffectiveAccessReviewResponse(userId, scope, organizationId, workspaceId, paths, permissionRows));
        }).RequireAuthorization("permissions.read").WithSummary("Reviews permission outcomes and group-to-role paths in the selected scope and its inherited scopes.");

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
            var items = entries.Select(e =>
            {
                var actor = e.ActorUserId is { } id ? names.GetValueOrDefault(id) : null;
                return new AuditEntryResponse(
                    e.Id, e.OccurredAtUtc, e.Action, e.Succeeded, e.ActorUserId, actor?.DisplayName,
                    e.TargetType, e.TargetId, e.IpAddress, e.Details, actor is null ? null : UserEndpoints.PhotoUrl(actor.Id, actor.PhotoVersion));
            }).ToList();
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
        new(u.Id, u.Email, u.DisplayName, u.Initials, u.EmailConfirmed, u.Disabled, u.LockedOut, u.Roles, u.CreatedAtUtc, u.LastSignInAtUtc, u.ActiveSessions, u.Organizations,
            UserEndpoints.PhotoUrl(u.Id, u.PhotoVersion));

    private static AccessControlRoleResponse ToResponse(AccessRoleView view) =>
        new(view.Role.Id, view.Role.Name, view.Role.Description, view.Role.Scope,
            view.Permissions.Select(permission => new AccessControlPermissionResponse(permission.Key, permission.Effect.ToString().ToLowerInvariant())).ToList(),
            view.Groups, view.Role.OrganizationId, view.Role.WorkspaceId);

    private static List<Domain.Authorization.RolePermissionDecision> ToPermissionAssignments(IReadOnlyList<AccessControlPermissionRequest>? permissions) =>
        (permissions ?? []).Select(permission => new Domain.Authorization.RolePermissionDecision(
            permission.Key ?? string.Empty,
            Enum.TryParse<Domain.Authorization.PermissionEffect>(permission.Effect, true, out var effect) ? effect : (Domain.Authorization.PermissionEffect)0)).ToList();

    private static AccessControlGroupResponse ToResponse(AccessGroupView view) =>
        new(view.Group.Id, view.Group.Name, view.Group.Description, view.MemberIds, view.RoleIds, view.Group.Scope, view.Group.OrganizationId, view.Group.WorkspaceId);

    private static bool GetLegacyScopedGrant(string key, OrganizationMember? organizationMember, WorkspaceMember? workspaceMember) => key switch
    {
        "organization.discover" or "private_chats.use" or "support.access" => organizationMember is { IsActive: true },
        "support.cases.read" or "support.cases.manage" => organizationMember is { IsActive: true }
            && (organizationMember.IsSupportAgent || organizationMember.Can(OrganizationPermission.Manage)),
        "organization.manage" => organizationMember?.Can(OrganizationPermission.Manage) == true,
        "organization.members.manage" => organizationMember?.Can(OrganizationPermission.ManageMembers) == true,
        "organization.roles.manage" => organizationMember?.Can(OrganizationPermission.ManageRoles) == true,
        "organization.workspaces.manage" => organizationMember?.Can(OrganizationPermission.ManageWorkspaces) == true,
        "channels.create" or "channels.manage" => workspaceMember is { IsActive: true, Role: WorkspaceRole.Administrator }
            || organizationMember?.Can(OrganizationPermission.ManageWorkspaces) == true,
        _ => false,
    };

    private static AdminOrganizationResponse ToResponse(AdminOrganizationView o) => new(o.Id, o.Name, o.CreatedAtUtc, o.Members, o.Owners,
        o.PhotoVersion is { } version ? $"/api/v1/admin/organizations/{o.Id}/photo?v={version}" : null);

    private static AdminOrganizationDetailResponse ToResponse(AdminOrganizationDetail d) => new(
        ToResponse(d.Organization),
        d.Members.Select(m => new AdminOrganizationMemberResponse(
            m.User.Id, m.User.DisplayName, m.User.Initials, m.Email, RoleName(m.Role), m.JoinedAtUtc, UserEndpoints.PhotoUrl(m.User.Id, m.User.PhotoVersion))).ToList());

    private static RuntimeSettingResponse ToResponse(RuntimeSettingView s) => new(s.Key, s.Group, s.Kind, s.Value, s.Source, s.Locked, s.Min, s.Max, s.Choices);

    private static ProviderGroupResponse ToResponse(ProviderGroupView g) =>
        new(g.Name, g.RestartRequired, g.Fields.Select(f => new ProviderFieldResponse(f.Key, f.Kind, f.Value, f.HasValue, f.Source, f.Locked, f.Choices)).ToList());

    private static async Task<bool> VerifyMigrationInventoryAsync(ChatXDbContext db, Guid jobId, string sourceProvider,
        string destinationProvider, IAttachmentStorageProviderRegistry providers, CancellationToken ct)
    {
        var hasUnfinishedUpload = await db.Attachments.AsNoTracking().AnyAsync(a =>
            (a.StorageProvider == sourceProvider || (a.StorageProvider == null && providers.DefaultProvider == sourceProvider))
            && !a.ContentRemoved && a.Status == AttachmentStatus.PendingUpload, ct).ConfigureAwait(false);
        if (hasUnfinishedUpload) return false;

        var current = await db.Attachments.AsNoTracking()
            .Where(a => (a.StorageProvider == sourceProvider || (a.StorageProvider == null && providers.DefaultProvider == sourceProvider))
                && !a.ContentRemoved && (a.Status == AttachmentStatus.Ready || a.Status == AttachmentStatus.Quarantined))
            .Select(a => new { a.Id, a.StorageKey, a.Size, a.Sha256 })
            .ToListAsync(ct).ConfigureAwait(false);
        var copied = await db.AttachmentStorageMigrationFiles.AsNoTracking()
            .Where(f => f.JobId == jobId && f.State == "Copied")
            .ToDictionaryAsync(f => f.AttachmentId, ct).ConfigureAwait(false);
        var destination = providers.Resolve(destinationProvider);
        foreach (var attachment in current)
        {
            if (!copied.TryGetValue(attachment.Id, out var file)
                || file.StorageKey != attachment.StorageKey || file.Size != attachment.Size || file.Sha256 != attachment.Sha256)
                return false;
            await using var stream = await destination.OpenReadAsync(attachment.StorageKey, ct).ConfigureAwait(false);
            if (stream is null) return false;
            var (size, hash) = await HashStorageContentAsync(stream, ct).ConfigureAwait(false);
            if (size != attachment.Size || !string.Equals(hash, attachment.Sha256, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static Task<int> MoveProviderReferencesAsync(ChatXDbContext db, string sourceProvider, string destinationProvider,
        string legacyProvider, CancellationToken ct) =>
        db.Attachments.Where(a => (a.StorageProvider == sourceProvider || (a.StorageProvider == null && legacyProvider == sourceProvider))
                && !a.ContentRemoved && (a.Status == AttachmentStatus.Ready || a.Status == AttachmentStatus.Quarantined))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.StorageProvider, destinationProvider), ct);

    private static async Task SaveStorageProviderAsync(ProviderSettingsManager settings, Guid actorId, string provider, CancellationToken ct) =>
        _ = await settings.SaveAsync(actorId, "storage", new Dictionary<string, JsonElement>
        {
            ["Storage:Provider"] = JsonSerializer.SerializeToElement(provider),
        }, ct).ConfigureAwait(false);

    private static async Task<(long Size, string Sha256)> HashStorageContentAsync(Stream input, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long size = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            size += read;
        }
        return (size, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static async Task<object> StorageMigrationJobResponseAsync(ChatXDbContext db, AttachmentStorageMigrationJob job, CancellationToken ct)
    {
        var counts = await db.AttachmentStorageMigrationFiles.AsNoTracking().Where(x => x.JobId == job.Id)
            .GroupBy(x => x.State).Select(g => new { State = g.Key, Count = g.LongCount() }).ToListAsync(ct).ConfigureAwait(false);
        long Count(string state) => counts.FirstOrDefault(x => x.State == state)?.Count ?? 0;
        return new
        {
            job.Id, job.SourceProvider, job.DestinationProvider, job.State, job.CreatedAtUtc, job.UpdatedAtUtc, job.LastError,
            TotalFiles = counts.Sum(x => x.Count), CopiedFiles = Count("Copied"), FailedFiles = Count("Failed"), SkippedFiles = Count("Skipped"),
            PendingFiles = Count("Pending") + Count("Copying"),
        };
    }
}
