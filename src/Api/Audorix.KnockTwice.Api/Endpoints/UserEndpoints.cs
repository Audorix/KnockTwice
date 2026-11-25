using Audorix.KnockTwice.Api.Identity;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Application.Installations;
using Audorix.KnockTwice.Application.Sessions;
using Audorix.KnockTwice.Application.Users;
using Audorix.KnockTwice.Contracts.V1.Admin;
using Audorix.KnockTwice.Contracts.V1.Users;
using Audorix.KnockTwice.Domain.Authorization;
using Audorix.KnockTwice.Infrastructure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Audorix.KnockTwice.Api.Endpoints;

/// <summary><c>/api/v1/users/me</c>: the signed-in user's profile, preferences and password.</summary>
internal static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder v1)
    {
        var me = v1.MapGroup("/users/me").RequireAuthorization().WithTags("Users");
        me.MapGet(string.Empty, GetMeAsync).WithSummary("The signed-in user's account, profile and preferences.");
        me.MapPut(string.Empty, UpdateProfileAsync).WithSummary("Updates the display name and, when given, the Display ID (handle; 400 handle_invalid or handle_taken).");
        me.MapPut("/preferences", UpdatePreferencesAsync).WithSummary("Updates user preferences (language, theme, desktop notifications); omitted fields stay unchanged.");
        me.MapPost("/password", ChangePasswordAsync).WithSummary("Changes the password and signs out other sessions.");
        // Profile photo and links (redesign 2026-11).
        me.MapPost("/photo", async (HttpContext http, ProfileMediaService media, UserProfileService profiles, CancellationToken ct) =>
        {
            var userId = http.User.RequireUserId();
            await MultipartUpload.ReadFileAsync(http, ProfileMediaService.MaxPhotoBytes, (_, _, stream) => media.SetPhotoAsync(userId, stream, null, ct), ct);
            return TypedResults.Ok(ToResponse(await profiles.GetCurrentAsync(userId, ct)));
        }).WithSummary("Sets the profile photo (multipart/form-data, field 'file'): PNG, JPEG or WebP, up to 1 MB, already cropped (the app sends a square image).");
        me.MapDelete("/photo", async (HttpContext http, ProfileMediaService media, UserProfileService profiles, CancellationToken ct) =>
        {
            var userId = http.User.RequireUserId();
            await media.RemovePhotoAsync(userId, ct);
            return TypedResults.Ok(ToResponse(await profiles.GetCurrentAsync(userId, ct)));
        }).WithSummary("Removes the profile photo; initials show again.");
        me.MapGet("/links", async (HttpContext http, ProfileMediaService media, CancellationToken ct) =>
            TypedResults.Ok((await media.ListOwnLinksAsync(http.User.RequireUserId(), ct)).Select(ToLink).ToList()))
            .WithSummary("Your profile links in order, with who sees each.");
        me.MapPut("/links", async (ReplaceProfileLinksRequest request, HttpContext http, ProfileMediaService media, CancellationToken ct) =>
            TypedResults.Ok((await media.ReplaceLinksAsync(http.User.RequireUserId(),
                (request.Links ?? []).Select(l => new ProfileLinkInput(l.Platform, l.Url, l.Visibility)).ToList(), ct)).Select(ToLink).ToList()))
            .WithSummary("Replaces your profile links (at most 10), in order. Platform links must point to that platform; only http(s) addresses.");

        var users = v1.MapGroup("/users/{userId:guid}").RequireAuthorization().WithTags("Users");
        users.MapGet("/photo", async (Guid userId, int? v, HttpContext http, ProfileMediaService media, CancellationToken ct) =>
        {
            var photo = await media.OpenPhotoAsync(userId, ct);
            if (photo is null)
            {
                return Results.NotFound();
            }

            AttachmentEndpoints.SetDownloadHeaders(http.Response, "photo", photo.ContentType, forceDownload: false, cacheable: true);
            // A versioned address never changes content: cache it in this browser for good.
            if (v == photo.Version)
            {
                http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            }

            return Results.Stream(photo.Content, photo.ContentType);
        }).WithSummary("A person's profile photo (any signed-in person). 404 when they show initials.");
        users.MapGet("/links", async (Guid userId, HttpContext http, ProfileMediaService media, CancellationToken ct) =>
            TypedResults.Ok((await media.ListVisibleLinksAsync(http.User.RequireUserId(), userId, ct)).Select(ToLink).ToList()))
            .WithSummary("The person's links you may see: those for contacts when you are their contact; none otherwise.");

        me.MapGet("/deletion", CheckDeletionAsync).WithSummary("Whether the account can be deleted now, and what stands in the way (organizations you are the only owner of).");
        me.MapPost("/deletion", DeleteAccountAsync)
            .WithSummary("Deletes the signed-in account after confirming the password (or, without one, the email address). Personal data is erased; messages stay as \"Deleted user\". Signs out everywhere.");

        v1.MapGet("/admin/installation", GetInstallationAsync)
            .RequireAuthorization(Permissions.InstallationRead)
            .WithTags("Administration")
            .WithSummary("Installation metadata. Requires the installation.read permission.");

        v1.MapPost("/admin/demo-data", async (HttpContext http, Application.Demo.DemoDataService demo, Administration.DemoDataJobs jobs, CancellationToken ct) =>
        {
            var job = jobs.StartLoad(http.User.RequireUserId(), demo);
            await Administration.DemoDataJobs.WaitAsync(job, ct);
            return TypedResults.Ok(ToResponse(job.Result!));
        })
            .RequireAuthorization(Permissions.DemoDataManage)
            .WithTags("Administration")
            .WithSummary("Populates demo data and waits until it is done (several minutes; prefer POST /admin/demo-data/jobs): 300 people (password Demo123!), 50 companies, contacts, 150 groups, 800 channels, direct conversations, messages and activity (mentions, saved messages, reminders, scheduled messages: 500 items for the caller, 50-100 for everyone else). Idempotent; only where DemoData:Enabled. The caller is included. Joins a load that is already running.");

        v1.MapPost("/admin/demo-data/jobs", async (string? action, HttpContext http, Application.Demo.DemoDataService demo, Administration.DemoDataJobs jobs, CancellationToken ct) =>
        {
            var job = action switch
            {
                null or "" or Administration.DemoDataJob.Load => jobs.StartLoad(http.User.RequireUserId(), demo),
                Administration.DemoDataJob.Clear => await jobs.StartClearAsync(http.User.RequireUserId(), demo, ct),
                _ => throw new Application.Common.RequestValidationException("action", "demo_data_action_invalid"),
            };
            return TypedResults.Accepted("/api/v1/admin/demo-data/jobs/current", ToResponse(job));
        })
            .RequireAuthorization(Permissions.DemoDataManage)
            .WithTags("Administration")
            .WithSummary("Starts loading (action=load, the default) or clearing (action=clear) demo data in the background and returns at once; follow it with GET /admin/demo-data/jobs/current. One job runs at a time: the same action joins it, the other is refused (demo_data_loading or demo_data_clearing).");

        v1.MapGet("/admin/demo-data/jobs/current", Results<Ok<DemoDataJobResponse>, NoContent> (Administration.DemoDataJobs jobs) =>
            jobs.Current is { } job ? TypedResults.Ok(ToResponse(job)) : TypedResults.NoContent())
            .RequireAuthorization(Permissions.DemoDataManage)
            .WithTags("Administration")
            .WithSummary("The running demo data job (load or clear) with progress per part, or the last one since the API started (204 when there was none).");

        v1.MapGet("/admin/demo-data", async (Application.Demo.DemoDataService demo, CancellationToken ct) =>
            TypedResults.Ok(ToCounts(await demo.CountAsync(ct))))
            .RequireAuthorization(Permissions.DemoDataManage)
            .WithTags("Administration")
            .WithSummary("How much demo data exists.");

        v1.MapDelete("/admin/demo-data", async (HttpContext http, Application.Demo.DemoDataService demo, Administration.DemoDataJobs jobs, CancellationToken ct) =>
        {
            var job = await jobs.StartClearAsync(http.User.RequireUserId(), demo, ct);
            await Administration.DemoDataJobs.WaitAsync(job, ct);
            return TypedResults.Ok(new DemoDataClearedResponse(ToCounts(job.Removed!)));
        })
            .RequireAuthorization(Permissions.DemoDataManage)
            .WithTags("Administration")
            .WithSummary("Removes only the demo data and waits until it is done (prefer POST /admin/demo-data/jobs?action=clear): demo people (and what they wrote), demo companies and demo conversations. Conversations and data real people created stay.");

        return v1;
    }

    /// <summary>The address of a profile photo; it changes with every new photo, so it can be cached for good.</summary>
    internal static string? PhotoUrl(Guid userId, int version) => version > 0 ? $"/api/v1/users/{userId}/photo?v={version}" : null;

    private static ProfileLinkResponse ToLink(Domain.Users.ProfileLink link) =>
        new(ProfileMediaService.PlatformName(link.Platform), link.Url, ProfileMediaService.VisibilityName(link.Visibility));

    private static DemoDataResponse ToResponse(Application.Demo.DemoDataResult result) =>
        new(ToCounts(result.Created), ToCounts(result.Total), result.UserEmails, result.Password);

    private static DemoDataJobResponse ToResponse(Administration.DemoDataJob job) => new(
        job.Id, job.State, job.StartedAtUtc, job.FinishedAtUtc,
        job.Steps().Select(s => new DemoDataStepResponse(s.Key, s.Done, s.Total)).ToList(),
        job.Result is { } result ? ToResponse(result) : null,
        job.ErrorCode,
        job.Kind,
        job.Removed is { } removed ? ToCounts(removed) : null);

    private static DemoDataCountsResponse ToCounts(Application.Demo.DemoDataCounts counts) =>
        new(counts.Users, counts.Organizations, counts.Contacts, counts.Conversations, counts.Messages, counts.Activity, counts.Support, counts.Photos);

    public static CurrentUserResponse ToResponse(CurrentUser user) => new(
        user.Account.Id,
        user.Account.Email,
        user.Account.EmailConfirmed,
        user.Profile.DisplayName,
        new AvatarResponse(user.Profile.Initials, user.Profile.HasPhoto ? PhotoUrl(user.Profile.UserId, user.Profile.PhotoVersion) : null),
        new UserPreferencesResponse(user.Profile.UiLanguage, user.Profile.Theme, user.Profile.DesktopNotifications, user.Profile.NotificationPreviews,
            user.Profile.EmailNotifications, user.Profile.ShareEmailWithContacts),
        user.Account.HasPassword,
        user.Account.ExternalProviders.Select(p => ExternalProviders.FindByScheme(p)?.Name ?? p.ToLowerInvariant()).ToList(),
        user.Account.Roles,
        Permissions.All.Where(p => Permissions.IsGrantedBy(user.Account.Roles, p)).ToList(),
        user.Profile.Handle);

    private static async Task<Ok<CurrentUserResponse>> GetMeAsync(HttpContext context, UserProfileService profiles, CancellationToken cancellationToken) =>
        TypedResults.Ok(ToResponse(await profiles.GetCurrentAsync(context.User.RequireUserId(), cancellationToken).ConfigureAwait(false)));

    private static async Task<Ok<CurrentUserResponse>> UpdateProfileAsync(
        UpdateProfileRequest request, HttpContext context, UserProfileService profiles, CancellationToken cancellationToken) =>
        TypedResults.Ok(ToResponse(await profiles.UpdateProfileAsync(context.User.RequireUserId(), request.DisplayName ?? string.Empty, cancellationToken, request.Handle)
            .ConfigureAwait(false)));

    private static async Task<Ok<CurrentUserResponse>> UpdatePreferencesAsync(
        UpdatePreferencesRequest request, HttpContext context, UserProfileService profiles, CancellationToken cancellationToken) =>
        TypedResults.Ok(ToResponse(await profiles.UpdatePreferencesAsync(
            context.User.RequireUserId(),
            // An empty request is still a (failed) language change, as before Phase 8.
            request is { Theme: null, DesktopNotifications: null, NotificationPreviews: null, EmailNotifications: null, ShareEmailWithContacts: null } ? request.UiLanguage ?? string.Empty : request.UiLanguage,
            cancellationToken,
            request.Theme,
            request.DesktopNotifications,
            request.NotificationPreviews,
            request.EmailNotifications,
            request.ShareEmailWithContacts).ConfigureAwait(false)));

    private static async Task<NoContent> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext context,
        AccountService accounts,
        SessionService sessions,
        UserManager<ChatXUser> users,
        SignInManager<ChatXUser> signIn,
        Application.Administration.AuditService audit,
        CancellationToken cancellationToken)
    {
        var userId = context.User.RequireUserId();
        var sessionId = context.User.RequireSessionId();
        await accounts.ChangePasswordAsync(userId, sessionId, request.CurrentPassword ?? string.Empty, request.NewPassword ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);
        await audit.RecordAsync(Application.Administration.AuditActions.PasswordChanged, userId, "user", userId.ToString(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // The password change rotated the security stamp; keep this session signed in with a fresh cookie.
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        var current = (await sessions.ListActiveAsync(userId, cancellationToken).ConfigureAwait(false)).FirstOrDefault(s => s.Id == sessionId);
        if (user is not null && current is not null)
        {
            await SessionSignIn.ReissueCookieAsync(signIn, user, sessionId, current.Persistent).ConfigureAwait(false);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Ok<AccountDeletionCheckResponse>> CheckDeletionAsync(HttpContext context, AccountDeletionService deletion, CancellationToken cancellationToken)
    {
        var check = await deletion.CheckAsync(context.User.RequireUserId(), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new AccountDeletionCheckResponse(
            check.CanDelete,
            check.SoleOwnerOf.Select(o => new DeletionBlockerResponse(o.Id, o.Name)).ToList(),
            check.LastAdministrator,
            check.RequiresPassword));
    }

    private static async Task<NoContent> DeleteAccountAsync(
        DeleteAccountRequest request,
        HttpContext context,
        AccountDeletionService deletion,
        SignInManager<ChatXUser> signIn,
        Application.PrivateChats.PrivateChatService privateChats,
        Application.Realtime.RealtimeService realtime,
        CancellationToken cancellationToken)
    {
        await deletion.DeleteOwnAsync(context.User.RequireUserId(), request.Password, request.ConfirmEmail, cancellationToken).ConfigureAwait(false);
        await realtime.CloseRevokedSessionsAsync(cancellationToken).ConfigureAwait(false);
        await privateChats.SweepAsync(cancellationToken).ConfigureAwait(false);
        await signIn.SignOutAsync().ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<InstallationResponse>, NotFound>> GetInstallationAsync(InstallationService installations, CancellationToken cancellationToken)
    {
        var installation = await installations.FindAsync(cancellationToken).ConfigureAwait(false);
        return installation is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new InstallationResponse(installation.Id, installation.DefaultLanguage, installation.InitializedAtUtc));
    }
}
