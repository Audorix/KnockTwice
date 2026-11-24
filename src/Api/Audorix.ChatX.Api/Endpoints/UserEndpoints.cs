using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Application.Installations;
using Audorix.ChatX.Application.Sessions;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Contracts.V1.Admin;
using Audorix.ChatX.Contracts.V1.Users;
using Audorix.ChatX.Domain.Authorization;
using Audorix.ChatX.Infrastructure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Audorix.ChatX.Api.Endpoints;

/// <summary><c>/api/v1/users/me</c>: the signed-in user's profile, preferences and password.</summary>
internal static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder v1)
    {
        var me = v1.MapGroup("/users/me").RequireAuthorization().WithTags("Users");
        me.MapGet(string.Empty, GetMeAsync).WithSummary("The signed-in user's account, profile and preferences.");
        me.MapPut(string.Empty, UpdateProfileAsync).WithSummary("Updates the display name.");
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

        v1.MapPost("/admin/demo-data", async (HttpContext http, Application.Demo.DemoDataService demo, Application.Administration.AuditService audit, CancellationToken ct) =>
        {
            var result = await demo.PopulateAsync(http.User.RequireUserId(), ct);
            await audit.RecordAsync(Application.Administration.AuditActions.DemoDataPopulated, http.User.RequireUserId(), cancellationToken: ct);
            return TypedResults.Ok(new DemoDataResponse(ToCounts(result.Created), ToCounts(result.Total), result.UserEmails, result.Password));
        })
            .RequireAuthorization(Permissions.DemoDataManage)
            .WithTags("Administration")
            .WithSummary("Populates demo data: 300 people (password Demo123!), 50 companies, contacts, 150 groups, 800 channels, direct conversations, messages and activity (mentions, saved messages, reminders, scheduled messages: 500 items for the caller, 50-100 for everyone else). Idempotent; only where DemoData:Enabled. The caller is included.");

        v1.MapGet("/admin/demo-data", async (Application.Demo.DemoDataService demo, CancellationToken ct) =>
            TypedResults.Ok(ToCounts(await demo.CountAsync(ct))))
            .RequireAuthorization(Permissions.DemoDataManage)
            .WithTags("Administration")
            .WithSummary("How much demo data exists.");

        v1.MapDelete("/admin/demo-data", async (HttpContext http, Application.Demo.DemoDataService demo, Application.Administration.AuditService audit, CancellationToken ct) =>
        {
            var removed = await demo.ClearAsync(ct);
            await audit.RecordAsync(Application.Administration.AuditActions.DemoDataCleared, http.User.RequireUserId(),
                details: new Dictionary<string, object?> { ["users"] = removed.Users, ["conversations"] = removed.Conversations, ["messages"] = removed.Messages },
                cancellationToken: ct);
            return TypedResults.Ok(new DemoDataClearedResponse(ToCounts(removed)));
        })
            .RequireAuthorization(Permissions.DemoDataManage)
            .WithTags("Administration")
            .WithSummary("Removes only the demo data: demo people (and what they wrote), demo companies and demo conversations. Conversations and data real people created stay.");

        return v1;
    }

    /// <summary>The address of a profile photo; it changes with every new photo, so it can be cached for good.</summary>
    internal static string? PhotoUrl(Guid userId, int version) => version > 0 ? $"/api/v1/users/{userId}/photo?v={version}" : null;

    private static ProfileLinkResponse ToLink(Domain.Users.ProfileLink link) =>
        new(ProfileMediaService.PlatformName(link.Platform), link.Url, ProfileMediaService.VisibilityName(link.Visibility));

    private static DemoDataCountsResponse ToCounts(Application.Demo.DemoDataCounts counts) =>
        new(counts.Users, counts.Organizations, counts.Contacts, counts.Conversations, counts.Messages, counts.Activity, counts.Support, counts.Photos);

    public static CurrentUserResponse ToResponse(CurrentUser user) => new(
        user.Account.Id,
        user.Account.Email,
        user.Account.EmailConfirmed,
        user.Profile.DisplayName,
        new AvatarResponse(user.Profile.Initials, user.Profile.HasPhoto ? PhotoUrl(user.Profile.UserId, user.Profile.PhotoVersion) : null),
        new UserPreferencesResponse(user.Profile.UiLanguage, user.Profile.Theme, user.Profile.DesktopNotifications, user.Profile.NotificationPreviews,
            user.Profile.EmailNotifications),
        user.Account.HasPassword,
        user.Account.ExternalProviders.Select(p => ExternalProviders.FindByScheme(p)?.Name ?? p.ToLowerInvariant()).ToList(),
        user.Account.Roles,
        Permissions.All.Where(p => Permissions.IsGrantedBy(user.Account.Roles, p)).ToList());

    private static async Task<Ok<CurrentUserResponse>> GetMeAsync(HttpContext context, UserProfileService profiles, CancellationToken cancellationToken) =>
        TypedResults.Ok(ToResponse(await profiles.GetCurrentAsync(context.User.RequireUserId(), cancellationToken).ConfigureAwait(false)));

    private static async Task<Ok<CurrentUserResponse>> UpdateProfileAsync(
        UpdateProfileRequest request, HttpContext context, UserProfileService profiles, CancellationToken cancellationToken) =>
        TypedResults.Ok(ToResponse(await profiles.UpdateProfileAsync(context.User.RequireUserId(), request.DisplayName ?? string.Empty, cancellationToken)
            .ConfigureAwait(false)));

    private static async Task<Ok<CurrentUserResponse>> UpdatePreferencesAsync(
        UpdatePreferencesRequest request, HttpContext context, UserProfileService profiles, CancellationToken cancellationToken) =>
        TypedResults.Ok(ToResponse(await profiles.UpdatePreferencesAsync(
            context.User.RequireUserId(),
            // An empty request is still a (failed) language change, as before Phase 8.
            request is { Theme: null, DesktopNotifications: null, NotificationPreviews: null, EmailNotifications: null } ? request.UiLanguage ?? string.Empty : request.UiLanguage,
            cancellationToken,
            request.Theme,
            request.DesktopNotifications,
            request.NotificationPreviews,
            request.EmailNotifications).ConfigureAwait(false)));

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
