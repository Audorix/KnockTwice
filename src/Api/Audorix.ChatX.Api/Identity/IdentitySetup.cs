using System.Security.Claims;
using System.Threading.RateLimiting;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Application.Sessions;
using Audorix.ChatX.Domain.Authorization;
using Audorix.ChatX.Infrastructure;
using Audorix.ChatX.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Api.Identity;

/// <summary>
/// Browser authentication: ASP.NET Core Identity with an HttpOnly, SameSite=Lax cookie that
/// carries a server-side session id (<see cref="ChatXClaims.SessionId"/>), antiforgery tokens on
/// every state-changing request, permission policies and per-IP rate limits on auth endpoints.
/// </summary>
internal static class IdentitySetup
{
    public const string AuthRateLimitPolicy = "auth";
    public const string CsrfHeaderName = "X-CSRF-TOKEN";

    public static IServiceCollection AddChatXIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccountSettings>().BindConfiguration(AccountSettings.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AccountSettings>, DelegatingValidator<AccountSettings>>(
            _ => new DelegatingValidator<AccountSettings>(s => s.Validate()));
        services.AddOptions<PublicUrlSettings>().BindConfiguration(PublicUrlSettings.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<PublicUrlSettings>, DelegatingValidator<PublicUrlSettings>>(
            _ => new DelegatingValidator<PublicUrlSettings>(s => s.Validate()));
        services.AddOptions<ExternalAuthenticationSettings>().BindConfiguration(ExternalAuthenticationSettings.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ExternalAuthenticationSettings>, DelegatingValidator<ExternalAuthenticationSettings>>(
            _ => new DelegatingValidator<ExternalAuthenticationSettings>(s => s.Validate()));

        services.AddIdentityCore<ChatXUser>()
            .AddRoles<ChatXRole>()
            .AddChatXIdentityStores()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddOptions<IdentityOptions>().Configure<IOptions<AccountSettings>>((identity, account) =>
        {
            var settings = account.Value;
            identity.User.RequireUniqueEmail = true;
            identity.SignIn.RequireConfirmedEmail = settings.RequireConfirmedEmail;
            // Length over composition rules (NIST SP 800-63B); the maximum is enforced by the API.
            identity.Password.RequiredLength = settings.PasswordMinimumLength;
            identity.Password.RequireDigit = false;
            identity.Password.RequireLowercase = false;
            identity.Password.RequireUppercase = false;
            identity.Password.RequireNonAlphanumeric = false;
            identity.Password.RequiredUniqueChars = 4;
            identity.Lockout.AllowedForNewUsers = true;
            identity.Lockout.MaxFailedAccessAttempts = settings.MaxFailedAccessAttempts;
            identity.Lockout.DefaultLockoutTimeSpan = settings.LockoutDuration;
        });
        services.AddOptions<DataProtectionTokenProviderOptions>().Configure<IOptions<AccountSettings>>(
            (tokens, account) => tokens.TokenLifespan = account.Value.TokenLifetime);
        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.FromMinutes(5);
            // Keep the session id when the principal is refreshed after a security-stamp check.
            options.OnRefreshingPrincipal = context =>
            {
                var sid = context.CurrentPrincipal?.FindFirst(ChatXClaims.SessionId);
                if (sid is not null && context.NewPrincipal?.Identity is ClaimsIdentity identity && !identity.HasClaim(c => c.Type == ChatXClaims.SessionId))
                {
                    identity.AddClaim(new Claim(ChatXClaims.SessionId, sid.Value));
                }

                return Task.CompletedTask;
            };
        });

        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        });
        var cookies = authentication.AddIdentityCookies();
        cookies.ApplicationCookie!.Configure<IOptions<AccountSettings>, IHostEnvironment>((cookie, account, environment) =>
        {
            cookie.Cookie.Name = "chatx.auth";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.Cookie.SecurePolicy = CookieSecurity(environment);
            // The server-side session enforces the real lifetime; the cookie only needs to outlive it.
            cookie.ExpireTimeSpan = account.Value.PersistentSessionLifetime;
            cookie.SlidingExpiration = true;
            cookie.Events = new CookieAuthenticationEvents
            {
                OnValidatePrincipal = ValidateSessionAsync,
                OnRedirectToLogin = context => WriteStatus(context.Response, StatusCodes.Status401Unauthorized),
                OnRedirectToAccessDenied = context => WriteStatus(context.Response, StatusCodes.Status403Forbidden),
            };
        });
        cookies.ExternalCookie!.Configure<IHostEnvironment>((cookie, environment) =>
        {
            cookie.Cookie.Name = "chatx.external";
            cookie.Cookie.SecurePolicy = CookieSecurity(environment);
            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        });

        var external = configuration.GetSection(ExternalAuthenticationSettings.SectionName).Get<ExternalAuthenticationSettings>() ?? new();
        if (external.Google.IsEnabled)
        {
            authentication.AddGoogle(ExternalProviders.Google.Scheme, options =>
            {
                options.ClientId = external.Google.ClientId!;
                options.ClientSecret = external.Google.ClientSecret!;
                options.CallbackPath = ExternalProviders.Google.CallbackPath;
                options.SignInScheme = IdentityConstants.ExternalScheme;
            });
        }

        if (external.Microsoft.IsEnabled)
        {
            authentication.AddMicrosoftAccount(ExternalProviders.Microsoft.Scheme, options =>
            {
                options.ClientId = external.Microsoft.ClientId!;
                options.ClientSecret = external.Microsoft.ClientSecret!;
                options.CallbackPath = ExternalProviders.Microsoft.CallbackPath;
                options.SignInScheme = IdentityConstants.ExternalScheme;
            });
        }

        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        var authorization = services.AddAuthorizationBuilder();
        foreach (var permission in Permissions.All)
        {
            authorization.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
        }

        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeaderName;
            options.Cookie.Name = "chatx.csrf";
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddOptions<RateLimitingSettings>().BindConfiguration(RateLimitingSettings.SectionName);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthRateLimitPolicy, context =>
            {
                var settings = context.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Phase 21: counted through IRateCounter, so the limit is shared by every API
                // instance when scale-out (Redis) is configured, and per instance otherwise.
                var counter = context.RequestServices.GetRequiredService<Application.Common.IRateCounter>();
                return RateLimitPartition.Get(
                    $"{address}|{settings.AuthPermitLimit}|{settings.AuthWindow.Ticks}",
                    _ => new ScaleOut.CounterRateLimiter(counter, $"auth:{address}", settings.AuthPermitLimit, settings.AuthWindow));
            });
        });

        return services;
    }

    private static CookieSecurePolicy CookieSecurity(IHostEnvironment environment) =>
        // Development allows plain-http localhost; everywhere else cookies require HTTPS.
        environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var userId = context.Principal?.GetUserId();
        var sessionId = context.Principal?.GetSessionId();
        var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionService>();
        if (userId is null || sessionId is null
            || !await sessions.ValidateAsync(sessionId.Value, userId.Value, context.HttpContext.RequestAborted).ConfigureAwait(false))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme).ConfigureAwait(false);
            return;
        }

        await SecurityStampValidator.ValidatePrincipalAsync(context).ConfigureAwait(false);
    }

    private static Task WriteStatus(HttpResponse response, int status)
    {
        response.StatusCode = status;
        return Task.CompletedTask;
    }
}

public static class ChatXClaims
{
    /// <summary>Server-side session identifier carried in the authentication cookie.</summary>
    public const string SessionId = "chatx:sid";

    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static Guid? GetSessionId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(SessionId), out var id) ? id : null;

    public static Guid RequireUserId(this ClaimsPrincipal principal) =>
        principal.GetUserId() ?? throw new InvalidOperationException("The authenticated principal has no user id.");

    public static Guid RequireSessionId(this ClaimsPrincipal principal) =>
        principal.GetSessionId() ?? throw new InvalidOperationException("The authenticated principal has no session id.");
}

/// <summary>Per-IP limits for anonymous authentication endpoints (ADR-018 rate limiting).</summary>
public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    public int AuthPermitLimit { get; set; } = 20;

    public TimeSpan AuthWindow { get; set; } = TimeSpan.FromMinutes(1);
}

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Grants a permission when any of the user's roles includes it. The roles are read from the
/// account (once per request), not from the cookie, so a revoked or granted installation role
/// takes effect on the next request (Phase 9). Without an HTTP request the cookie's roles are used.
/// </summary>
internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private const string RolesItemKey = "chatx.roles";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        IEnumerable<string> roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);
        if (context.Resource is HttpContext http && context.User.GetUserId() is { } userId)
        {
            if (http.Items[RolesItemKey] is not IReadOnlyList<string> current)
            {
                var account = await http.RequestServices.GetRequiredService<Application.Identity.IUserAccountManager>()
                    .FindByIdAsync(userId, http.RequestAborted).ConfigureAwait(false);
                current = account?.Roles ?? [];
                http.Items[RolesItemKey] = current;
            }

            roles = current;
        }

        if (Permissions.IsGrantedBy(roles, requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}

internal sealed class DelegatingValidator<T>(Func<T, IReadOnlyList<string>> validate) : IValidateOptions<T>
    where T : class
{
    public ValidateOptionsResult Validate(string? name, T options)
    {
        var errors = validate(options);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
