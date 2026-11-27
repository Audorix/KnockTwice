using Audorix.KnockTwice.Application.Administration;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Audorix.KnockTwice.Api.Identity;
using Audorix.KnockTwice.Application.Abstractions;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Application.Realtime;
using Audorix.KnockTwice.Application.Sessions;
using Audorix.KnockTwice.Application.Users;
using Audorix.KnockTwice.Application.Organizations;
using Audorix.KnockTwice.Contracts.Errors;
using Audorix.KnockTwice.Contracts.V1.Auth;
using Audorix.KnockTwice.Contracts.V1.Users;
using Audorix.KnockTwice.Domain;
using Audorix.KnockTwice.Domain.Organizations;
using Audorix.KnockTwice.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Api.Endpoints;

/// <summary>
/// <c>/api/v1/auth</c>: configuration, CSRF token, registration, email confirmation, sign-in/out,
/// password recovery, external providers and session management.
/// </summary>
internal static class AuthEndpoints
{
    private static readonly AcceptedResponse Accepted = new("accepted");

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder v1)
    {
        var auth = v1.MapGroup("/auth").WithTags("Authentication");

        auth.MapGet("/config", GetConfiguration).WithSummary("Public sign-in configuration (captcha site key, enabled providers).");
        auth.MapGet("/csrf", GetCsrfToken).WithSummary("Issues the antiforgery token required on state-changing requests.");

        var limited = auth.MapGroup(string.Empty).RequireRateLimiting(IdentitySetup.AuthRateLimitPolicy);
        limited.MapPost("/register", RegisterAsync).WithSummary("Creates a local account and sends a confirmation email.");
        limited.MapPost("/confirm-email", ConfirmEmailAsync).WithSummary("Confirms an email address from an emailed link.");
        limited.MapPost("/resend-confirmation", ResendConfirmationAsync).WithSummary("Sends a new confirmation link if the account is unconfirmed.");
        limited.MapPost("/login", LoginAsync).WithSummary("Signs in with email and password; sets the session cookie.");
        limited.MapPost("/forgot-password", ForgotPasswordAsync).WithSummary("Emails a password reset link if the account exists.");
        limited.MapPost("/reset-password", ResetPasswordAsync).WithSummary("Sets a new password from an emailed link; signs out all sessions.");
        limited.MapGet("/external/{provider}", ExternalChallenge).WithSummary("Starts sign-in with an enabled external provider (google, microsoft).");
        limited.MapGet("/external/callback", ExternalCallbackAsync).WithSummary("Completes external sign-in and redirects to the web app.").ExcludeFromDescription();
        limited.MapGet("/organization-sso/{organizationId:guid}", OrganizationSsoChallengeAsync)
            .WithSummary("Starts sign-in through an organization's configured identity provider.");
        limited.MapGet("/organization-sso/complete/{organizationId:guid}", OrganizationSsoCompleteAsync)
            .WithSummary("Completes an organization SSO sign-in.").ExcludeFromDescription();

        auth.MapPost("/logout", LogoutAsync).RequireAuthorization().WithSummary("Ends the current session.");

        var sessions = auth.MapGroup("/sessions").RequireAuthorization();
        sessions.MapGet(string.Empty, ListSessionsAsync).WithSummary("Lists the caller's active sessions.");
        sessions.MapDelete("/{sessionId:guid}", RevokeSessionAsync).WithSummary("Revokes one of the caller's sessions.");
        sessions.MapPost("/revoke-others", RevokeOtherSessionsAsync).WithSummary("Revokes every session except the current one.");

        return v1;
    }

    private static Ok<AuthConfigurationResponse> GetConfiguration(
        IOptionsSnapshot<AccountSettings> account,
        IOptions<ExternalAuthenticationSettings> external,
        IOptions<Application.Demo.DemoDataSettings> demo,
        ICaptchaVerifier captcha)
    {
        var settings = account.Value;
        return TypedResults.Ok(new AuthConfigurationResponse(
            settings.EffectiveRegistrationMode != RegistrationModes.Closed,
            settings.RequireConfirmedEmail,
            settings.PasswordMinimumLength,
            new CaptchaConfigurationResponse(captcha.ClientConfiguration.Provider, captcha.ClientConfiguration.SiteKey),
            external.Value.EnabledProviders,
            settings.AutoConfirmEmail,
            demo.Value.Enabled,
            settings.EffectiveRegistrationMode));
    }

    private static Ok<CsrfTokenResponse> GetCsrfToken(HttpContext context, IAntiforgery antiforgery)
    {
        context.Response.Headers.CacheControl = "no-store";
        var tokens = antiforgery.GetAndStoreTokens(context);
        return TypedResults.Ok(new CsrfTokenResponse(tokens.RequestToken!, IdentitySetup.CsrfHeaderName));
    }

    private static async Task<Accepted<AcceptedResponse>> RegisterAsync(
        RegisterRequest request, HttpContext context, AccountService accounts, AuditService audit, CancellationToken cancellationToken)
    {
        await accounts.RegisterAsync(
            new RegisterCommand(request.Email, request.Password, request.DisplayName, request.Language, request.CaptchaToken, RemoteIp(context)),
            cancellationToken).ConfigureAwait(false);

        // Recorded without the address: the response must not reveal whether it was a new account.
        await audit.RecordAsync(AuditActions.Registered, null, cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.Accepted((string?)null, Accepted);
    }

    private static async Task<NoContent> ConfirmEmailAsync(ConfirmEmailRequest request, AccountService accounts, AuditService audit, CancellationToken cancellationToken)
    {
        await accounts.ConfirmEmailAsync(request.UserId, request.Token ?? string.Empty, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.EmailConfirmed, request.UserId, "user", request.UserId.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Accepted<AcceptedResponse>> ResendConfirmationAsync(
        EmailActionRequest request, HttpContext context, AccountService accounts, CancellationToken cancellationToken)
    {
        await accounts.ResendConfirmationAsync(request.Email ?? string.Empty, request.CaptchaToken, RemoteIp(context), cancellationToken).ConfigureAwait(false);
        return TypedResults.Accepted((string?)null, Accepted);
    }

    private static async Task<Accepted<AcceptedResponse>> ForgotPasswordAsync(
        EmailActionRequest request, HttpContext context, AccountService accounts, CancellationToken cancellationToken)
    {
        await accounts.ForgotPasswordAsync(request.Email ?? string.Empty, request.CaptchaToken, RemoteIp(context), cancellationToken).ConfigureAwait(false);
        return TypedResults.Accepted((string?)null, Accepted);
    }

    private static async Task<NoContent> ResetPasswordAsync(ResetPasswordRequest request, AccountService accounts, AuditService audit, CancellationToken cancellationToken)
    {
        await accounts.ResetPasswordAsync(request.UserId, request.Token ?? string.Empty, request.NewPassword ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.PasswordReset, request.UserId, "user", request.UserId.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<CurrentUserResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        HttpContext context,
        UserManager<ChatXUser> users,
        SignInManager<ChatXUser> signIn,
        SessionService sessions,
        UserProfileService profiles,
        AuditService audit,
        OrganizationDomainEnrollmentService domainEnrollment,
        CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim();
        var user = string.IsNullOrEmpty(email) || string.IsNullOrEmpty(request.Password) || request.Password.Length > AccountService.PasswordMaxLength
            ? null
            : await users.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null)
        {
            // The attempted address is not recorded: it may be personal data of someone without an account.
            await AuditSignInAsync(audit, null, false, "unknown_account", cancellationToken).ConfigureAwait(false);
            return Problem(StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials);
        }

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true).ConfigureAwait(false);
        if (result.IsNotAllowed)
        {
            // Only reachable with the correct password, so it does not reveal account existence.
            await AuditSignInAsync(audit, user.Id, false, "email_not_confirmed", cancellationToken).ConfigureAwait(false);
            return Problem(StatusCodes.Status403Forbidden, ErrorCodes.EmailNotConfirmed);
        }

        if (!result.Succeeded)
        {
            // Wrong password, lock-out and disabled accounts are indistinguishable to the caller.
            var reason = user.DisabledAtUtc is not null ? "disabled" : result.IsLockedOut ? "locked_out" : "wrong_password";
            await AuditSignInAsync(audit, user.Id, false, reason, cancellationToken).ConfigureAwait(false);
            return Problem(StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials);
        }

        await SessionSignIn.SignInAsync(context, signIn, sessions, user, request.RememberMe, cancellationToken).ConfigureAwait(false);
        await domainEnrollment.EnrollIfEligibleAsync(user.Id, cancellationToken).ConfigureAwait(false);
        await AuditSignInAsync(audit, user.Id, true, null, cancellationToken).ConfigureAwait(false);
        var current = await profiles.GetCurrentAsync(user.Id, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(UserEndpoints.ToResponse(current));
    }

    private static async Task<NoContent> LogoutAsync(
        HttpContext context, SignInManager<ChatXUser> signIn, SessionService sessions, RealtimeService realtime,
        Application.PrivateChats.PrivateChatService privateChats, AuditService audit, CancellationToken cancellationToken)
    {
        var userId = context.User.RequireUserId();
        await audit.RecordAsync(AuditActions.SignOut, userId, "user", userId.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (context.User.GetSessionId() is { } sessionId)
        {
            await sessions.RevokeAsync(userId, sessionId, cancellationToken).ConfigureAwait(false);

            // Private chats live as long as the session that started them; end them while the
            // connections are still open, so both people are told.
            await privateChats.EndForSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            realtime.CloseSession(sessionId);
        }

        await signIn.SignOutAsync().ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static Results<ChallengeHttpResult, ProblemHttpResult> ExternalChallenge(
        string provider, string? returnUrl, IOptions<ExternalAuthenticationSettings> external, SignInManager<ChatXUser> signIn)
    {
        var selected = ExternalProviders.Find(provider);
        if (selected is null || !external.Value.EnabledProviders.Contains(selected.Name))
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.ExternalProviderNotEnabled);
        }

        var callback = $"/api/v1/auth/external/callback?returnUrl={Uri.EscapeDataString(LocalReturnUrl.Normalize(returnUrl))}";
        var properties = signIn.ConfigureExternalAuthenticationProperties(selected.Scheme, callback);
        return TypedResults.Challenge(properties, [selected.Scheme]);
    }

    private static async Task<RedirectHttpResult> ExternalCallbackAsync(
        string? returnUrl,
        string? remoteError,
        HttpContext context,
        UserManager<ChatXUser> users,
        SignInManager<ChatXUser> signIn,
        SessionService sessions,
        AccountService accounts,
        IOptions<PublicUrlSettings> urls,
        OrganizationDomainEnrollmentService domainEnrollment,
        CancellationToken cancellationToken)
    {
        RedirectHttpResult Fail(string code) => TypedResults.Redirect(urls.Value.Link("sign-in", ("error", code)).AbsoluteUri);

        if (!string.IsNullOrEmpty(remoteError))
        {
            return Fail("external_sign_in_cancelled");
        }

        var info = await signIn.GetExternalLoginInfoAsync().ConfigureAwait(false);
        if (info is null || ExternalProviders.FindByScheme(info.LoginProvider) is null)
        {
            return Fail("external_sign_in_failed");
        }

        try
        {
            var user = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey).ConfigureAwait(false);
            if (user is null)
            {
                var provisioned = await accounts.ProvisionExternalAccountAsync(
                    new ExternalLoginReference(info.LoginProvider, info.ProviderKey, info.Principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty),
                    info.Principal.FindFirstValue(ClaimTypes.Email),
                    context.Request.Headers.AcceptLanguage.ToString().Split(',', 2)[0].Split('-', 2)[0],
                    cancellationToken).ConfigureAwait(false);
                if (!provisioned.Succeeded)
                {
                    return Fail(provisioned.ErrorCode!);
                }

                user = await users.FindByIdAsync(provisioned.UserId!.Value.ToString()).ConfigureAwait(false);
            }

            if (user is null || await users.IsLockedOutAsync(user).ConfigureAwait(false))
            {
                return Fail("external_sign_in_failed");
            }

            await SessionSignIn.SignInAsync(context, signIn, sessions, user, persistent: true, cancellationToken).ConfigureAwait(false);
            await domainEnrollment.EnrollIfEligibleAsync(user.Id, cancellationToken).ConfigureAwait(false);
            return TypedResults.Redirect(new Uri(urls.Value.BaseUri, LocalReturnUrl.Normalize(returnUrl).TrimStart('/')).AbsoluteUri);
        }
        finally
        {
            await context.SignOutAsync(IdentityConstants.ExternalScheme).ConfigureAwait(false);
        }
    }

    private static async Task<Results<ChallengeHttpResult, RedirectHttpResult>> OrganizationSsoChallengeAsync(
        Guid organizationId, string? returnUrl, OrganizationDomainService organizations,
        IDataProtectionProvider protection, OrganizationOidcSchemeManager schemes, IOptions<PublicUrlSettings> urls,
        CancellationToken cancellationToken)
    {
        var settings = await organizations.GetSsoForSignInAsync(organizationId, cancellationToken).ConfigureAwait(false);
        if (settings is null)
            return TypedResults.Redirect(urls.Value.Link("sign-in", ("error", "external_sign_in_failed")).AbsoluteUri);

        try
        {
            var secret = protection.CreateProtector("Audorix.KnockTwice.OrganizationSso.ClientSecret.v1")
                .Unprotect(settings.ProtectedClientSecret);
            await schemes.EnsureRegisteredAsync(settings, secret).ConfigureAwait(false);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return TypedResults.Redirect(urls.Value.Link("sign-in", ("error", "external_sign_in_failed")).AbsoluteUri);
        }

        var safeReturnUrl = LocalReturnUrl.Normalize(returnUrl);
        var complete = $"/api/v1/auth/organization-sso/complete/{organizationId:D}?returnUrl={Uri.EscapeDataString(safeReturnUrl)}";
        var properties = new AuthenticationProperties { RedirectUri = complete };
        var scheme = OrganizationOidcSchemeRegistry.SchemeName(organizationId);
        return TypedResults.Challenge(properties, [scheme]);
    }

    private static async Task<RedirectHttpResult> OrganizationSsoCompleteAsync(
        Guid organizationId, string? returnUrl, HttpContext context, UserManager<ChatXUser> users,
        SignInManager<ChatXUser> signIn, AccountService accounts, SessionService sessions,
        OrganizationDomainService organizations, OrganizationDomainEnrollmentService enrollment,
        AuditService audit, IOptions<PublicUrlSettings> urls, CancellationToken cancellationToken)
    {
        RedirectHttpResult Fail() => TypedResults.Redirect(urls.Value.Link("sign-in", ("error", "external_sign_in_failed")).AbsoluteUri);
        try
        {
            var settings = await organizations.GetSsoForSignInAsync(organizationId, cancellationToken).ConfigureAwait(false);
            var info = await signIn.GetExternalLoginInfoAsync().ConfigureAwait(false);
            if (settings is null || info is null
                || !string.Equals(info.LoginProvider, OrganizationOidcSchemeRegistry.SchemeName(organizationId), StringComparison.Ordinal))
                return Fail();

            var email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? info.Principal.FindFirstValue("email");
            var subject = info.ProviderKey;
            var verified = info.Principal.FindFirstValue("email_verified");
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(subject)
                || !string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase)
                || !EmailMatchesOrganizationDomain(email, settings.VerifiedDomains))
                return Fail();

            var loginProvider = $"orgsso-{organizationId:N}";
            var providerKey = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(settings.Issuer + "\n" + subject)));
            var user = await users.FindByLoginAsync(loginProvider, providerKey).ConfigureAwait(false);
            if (user is null)
            {
                user = await users.FindByEmailAsync(email.Trim()).ConfigureAwait(false);
                if (user is not null)
                {
                    if (!user.EmailConfirmed || !string.Equals(user.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)) return Fail();
                    var linked = await users.AddLoginAsync(user, new UserLoginInfo(loginProvider, providerKey, settings.OrganizationName)).ConfigureAwait(false);
                    if (!linked.Succeeded) return Fail();
                }
                else
                {
                    if (!settings.AutomaticEnrollmentEnabled) return Fail();
                    var displayName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? info.Principal.FindFirstValue("name") ?? string.Empty;
                    var provisioned = await accounts.ProvisionExternalAccountAsync(
                        new ExternalLoginReference(loginProvider, providerKey, displayName), email,
                        context.Request.Headers.AcceptLanguage.ToString().Split(',', 2)[0].Split('-', 2)[0], cancellationToken).ConfigureAwait(false);
                    if (!provisioned.Succeeded) return Fail();
                    user = await users.FindByIdAsync(provisioned.UserId!.Value.ToString()).ConfigureAwait(false);
                }
            }

            if (user is null || !user.EmailConfirmed || user.DisabledAtUtc is not null
                || await users.IsLockedOutAsync(user).ConfigureAwait(false)) return Fail();

            await enrollment.EnrollIfEligibleAsync(user.Id, cancellationToken).ConfigureAwait(false);
            if (!await enrollment.HasActiveMembershipAsync(organizationId, user.Id, cancellationToken).ConfigureAwait(false)) return Fail();

            await SessionSignIn.SignInAsync(context, signIn, sessions, user, persistent: true, cancellationToken).ConfigureAwait(false);
            await audit.RecordAsync(AuditActions.SignIn, user.Id, "organization", organizationId.ToString(),
                new Dictionary<string, object?> { ["provider"] = "organization_sso" }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return TypedResults.Redirect(new Uri(urls.Value.BaseUri, LocalReturnUrl.Normalize(returnUrl).TrimStart('/')).AbsoluteUri);
        }
        finally
        {
            await context.SignOutAsync(IdentityConstants.ExternalScheme).ConfigureAwait(false);
        }
    }

    private static bool EmailMatchesOrganizationDomain(string email, IReadOnlyList<string> verifiedDomains)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0 || at == email.Length - 1) return false;
        string domain;
        try { domain = VerifiedOrganizationDomain.NormalizeDomain(email[(at + 1)..]); }
        catch (DomainRuleException) { return false; }
        return verifiedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<Ok<IReadOnlyList<SessionResponse>>> ListSessionsAsync(
        HttpContext context, SessionService sessions, CancellationToken cancellationToken)
    {
        var current = context.User.GetSessionId();
        var list = await sessions.ListActiveAsync(context.User.RequireUserId(), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SessionResponse> response = list
            .Select(s => new SessionResponse(s.Id, s.Description, s.CreatedAtUtc, s.LastSeenAtUtc, s.ExpiresAtUtc, s.Persistent, s.Id == current))
            .ToList();
        return TypedResults.Ok(response);
    }

    private static async Task<NoContent> RevokeSessionAsync(
        Guid sessionId, HttpContext context, SessionService sessions, SignInManager<ChatXUser> signIn, RealtimeService realtime,
        Application.PrivateChats.PrivateChatService privateChats, AuditService audit, CancellationToken cancellationToken)
    {
        await sessions.RevokeAsync(context.User.RequireUserId(), sessionId, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.SessionRevoked, context.User.RequireUserId(), "session", sessionId.ToString(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await privateChats.EndForSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        realtime.CloseSession(sessionId);
        if (context.User.GetSessionId() == sessionId)
        {
            await signIn.SignOutAsync().ConfigureAwait(false);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Ok<RevokedSessionsResponse>> RevokeOtherSessionsAsync(
        HttpContext context, SessionService sessions, RealtimeService realtime, Application.PrivateChats.PrivateChatService privateChats,
        AuditService audit, CancellationToken cancellationToken)
    {
        var revoked = await sessions.RevokeAllAsync(context.User.RequireUserId(), context.User.RequireSessionId(), cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.OtherSessionsRevoked, context.User.RequireUserId(), "user", context.User.RequireUserId().ToString(),
            new Dictionary<string, object?> { ["count"] = revoked }, cancellationToken: cancellationToken).ConfigureAwait(false);
        await privateChats.SweepAsync(cancellationToken).ConfigureAwait(false);
        await realtime.CloseRevokedSessionsAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new RevokedSessionsResponse(revoked));
    }

    private static Task AuditSignInAsync(AuditService audit, Guid? userId, bool succeeded, string? reason, CancellationToken cancellationToken) =>
        audit.RecordAsync(AuditActions.SignIn, userId, userId is null ? null : "user", userId?.ToString(),
            reason is null ? null : new Dictionary<string, object?> { ["reason"] = reason }, succeeded, cancellationToken);

    internal static ProblemHttpResult Problem(int status, string code) =>
        TypedResults.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static string? RemoteIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString();
}

/// <summary>Starts a server-side session and issues the authentication cookie that references it.</summary>
internal static class SessionSignIn
{
    public static async Task SignInAsync(
        HttpContext context, SignInManager<ChatXUser> signIn, SessionService sessions, ChatXUser user, bool persistent, CancellationToken cancellationToken)
    {
        var session = await sessions.StartAsync(
            user.Id, ClientDescription.FromUserAgent(context.Request.Headers.UserAgent), persistent, cancellationToken).ConfigureAwait(false);
        await ReissueCookieAsync(signIn, user, session.Id, persistent).ConfigureAwait(false);
    }

    /// <summary>Issues a fresh cookie for an existing session (for example after the security stamp changed).</summary>
    public static Task ReissueCookieAsync(SignInManager<ChatXUser> signIn, ChatXUser user, Guid sessionId, bool persistent) =>
        signIn.SignInWithClaimsAsync(user, new AuthenticationProperties { IsPersistent = persistent }, [new Claim(ChatXClaims.SessionId, sessionId.ToString())]);
}
