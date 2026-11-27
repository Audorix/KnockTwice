using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using Audorix.KnockTwice.Application.Organizations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Api.Identity;

internal sealed record OrganizationOidcSchemeSettings(string Issuer, string ClientId, string ClientSecret, string CallbackPath);

internal sealed class OrganizationOidcSchemeRegistry
{
    private readonly ConcurrentDictionary<string, OrganizationOidcSchemeSettings> _settings = new(StringComparer.Ordinal);

    public static string SchemeName(Guid organizationId) => $"OrganizationSso-{organizationId:N}";

    public static string CallbackPath(Guid organizationId) => $"/api/v1/auth/organization-sso/callback/{organizationId:D}";

    public void Set(string scheme, OrganizationOidcSchemeSettings settings) => _settings[scheme] = settings;

    public bool TryGet(string scheme, out OrganizationOidcSchemeSettings settings) => _settings.TryGetValue(scheme, out settings!);

    public void Remove(string scheme) => _settings.TryRemove(scheme, out _);
}

internal sealed class OrganizationOidcSchemeManager(
    OrganizationOidcSchemeRegistry registry,
    IAuthenticationSchemeProvider schemes,
    IOptionsMonitorCache<OpenIdConnectOptions> optionsCache,
    IOptionsMonitor<OpenIdConnectOptions> options)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task EnsureRegisteredAsync(OrganizationSsoSignInSettings settings, string clientSecret)
    {
        var name = OrganizationOidcSchemeRegistry.SchemeName(settings.OrganizationId);
        var gate = _locks.GetOrAdd(name, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            registry.Set(name, new OrganizationOidcSchemeSettings(settings.Issuer, settings.ClientId, clientSecret,
                OrganizationOidcSchemeRegistry.CallbackPath(settings.OrganizationId)));
            if (await schemes.GetSchemeAsync(name).ConfigureAwait(false) is null)
                schemes.AddScheme(new AuthenticationScheme(name, name, typeof(OpenIdConnectHandler)));
            optionsCache.TryRemove(name);
            _ = options.Get(name); // Materialize and validate the per-organization options before redirecting.
        }
        finally
        {
            gate.Release();
        }
    }

    public void Remove(Guid organizationId)
    {
        var name = OrganizationOidcSchemeRegistry.SchemeName(organizationId);
        registry.Remove(name);
        schemes.RemoveScheme(name);
        optionsCache.TryRemove(name);
    }
}

internal sealed class ConfigureOrganizationOidcOptions(OrganizationOidcSchemeRegistry registry) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    private const string SchemePrefix = "OrganizationSso-";

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name is null || !name.StartsWith(SchemePrefix, StringComparison.Ordinal) || !registry.TryGet(name, out var settings)) return;

        options.Authority = settings.Issuer;
        options.ClientId = settings.ClientId;
        options.ClientSecret = settings.ClientSecret;
        options.CallbackPath = settings.CallbackPath;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.ResponseType = "code";
        options.ResponseMode = "form_post";
        options.UsePkce = true;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = true;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.ClaimActions.MapUniqueJsonKey(ClaimTypes.NameIdentifier, "sub");
        options.ClaimActions.MapUniqueJsonKey(ClaimTypes.Name, "name");
        options.ClaimActions.MapUniqueJsonKey(ClaimTypes.Email, "email");
        options.ClaimActions.MapUniqueJsonKey("email_verified", "email_verified");
        options.Events = new OpenIdConnectEvents
        {
            OnRemoteFailure = context =>
            {
                context.Response.Redirect("/sign-in?error=organization_sso_failed");
                context.HandleResponse();
                return Task.CompletedTask;
            },
        };
    }

    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);
}

internal static class OrganizationOidcCallbackRegistration
{
    public static IApplicationBuilder UseOrganizationOidcCallbackRegistration(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        const string prefix = "/api/v1/auth/organization-sso/callback/";
        var path = context.Request.Path.Value;
        if (path is not null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(path[prefix.Length..], out var organizationId))
        {
            var service = context.RequestServices.GetRequiredService<OrganizationDomainService>();
            var settings = await service.GetSsoForSignInAsync(organizationId, context.RequestAborted).ConfigureAwait(false);
            if (settings is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            try
            {
                var protector = context.RequestServices.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("Audorix.KnockTwice.OrganizationSso.ClientSecret.v1");
                var secret = protector.Unprotect(settings.ProtectedClientSecret);
                await context.RequestServices.GetRequiredService<OrganizationOidcSchemeManager>()
                    .EnsureRegisteredAsync(settings, secret).ConfigureAwait(false);
            }
            catch (CryptographicException)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }

        await next(context).ConfigureAwait(false);
    });
}
