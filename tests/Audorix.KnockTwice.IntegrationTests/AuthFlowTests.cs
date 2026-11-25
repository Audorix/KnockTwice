using System.Net;
using System.Text.Json;
using Audorix.KnockTwice.Application.Abstractions;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Audorix.KnockTwice.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// Phase 2 acceptance flows through the real HTTP pipeline (cookies, antiforgery, Identity,
/// sessions) against real SQLite and PostgreSQL databases. Only email delivery and the captcha
/// provider are replaced by in-memory fakes.
/// </summary>
public sealed class AuthFlowTests
{
    private const string Password = "correct horse battery";
    private const string NewPassword = "new staple battery horse";

    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Register_confirm_sign_in_profile_and_sign_out(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        using var browser = new ApiSession(api.CreateHttpsClient());

        var register = await browser.PostAsync("/api/v1/auth/register",
            new { email = "ada@example.org", password = Password, displayName = "Ada Lovelace", language = "es", captchaToken = "t" });
        AssertStatus(HttpStatusCode.Accepted, register, await register.Content.ReadAsStringAsync(Ct));
        Assert.Contains(CaptchaActions.Register, api.Captcha.Actions);

        // Unconfirmed accounts cannot sign in (only revealed with the correct password).
        var early = await browser.LoginAsync("ada@example.org", Password);
        Assert.Equal(HttpStatusCode.Forbidden, early.StatusCode);
        Assert.Equal("email_not_confirmed", await ErrorCodeAsync(early));

        // The confirmation email is localized to the language chosen at registration.
        var email = Assert.Single(api.Emails.To("ada@example.org", "email-confirmation"));
        Assert.Contains("Hola, Ada Lovelace", email.TextBody, StringComparison.Ordinal);
        var (userId, token) = api.Emails.LatestLink("ada@example.org", "email-confirmation", "verify-email");

        AssertStatus(HttpStatusCode.BadRequest, await browser.PostAsync("/api/v1/auth/confirm-email", new { userId, token = token + "x" }));
        AssertStatus(HttpStatusCode.NoContent, await browser.PostAsync("/api/v1/auth/confirm-email", new { userId, token }));

        var login = await browser.LoginAsync("ada@example.org", Password, rememberMe: true);
        AssertStatus(HttpStatusCode.OK, login, await login.Content.ReadAsStringAsync(Ct));
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("chatx.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await JsonAsync(await browser.Client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative), Ct));
        Assert.Equal(userId, me.GetProperty("id").GetGuid());
        Assert.True(me.GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal("AL", me.GetProperty("avatar").GetProperty("initials").GetString());
        Assert.Equal("es", me.GetProperty("preferences").GetProperty("uiLanguage").GetString());

        var renamed = await browser.SendAsync(HttpMethod.Put, "/api/v1/users/me", new { displayName = "Augusta Ada" });
        Assert.Equal("Augusta Ada", (await JsonAsync(renamed)).GetProperty("displayName").GetString());
        var language = await browser.SendAsync(HttpMethod.Put, "/api/v1/users/me/preferences", new { uiLanguage = "en" });
        Assert.Equal("en", (await JsonAsync(language)).GetProperty("preferences").GetProperty("uiLanguage").GetString());
        var unsupported = await browser.SendAsync(HttpMethod.Put, "/api/v1/users/me/preferences", new { uiLanguage = "fr" });
        Assert.Equal("validation_failed", await ErrorCodeAsync(unsupported));

        AssertStatus(HttpStatusCode.NoContent, await browser.PostAsync("/api/v1/auth/logout"));
        AssertStatus(HttpStatusCode.Unauthorized, await browser.Client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative), Ct));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Password_reset_replaces_password_and_ends_every_session(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        await CreateConfirmedUserAsync(api, "grace@example.org");
        using var laptop = new ApiSession(api.CreateHttpsClient());
        using var phone = new ApiSession(api.CreateHttpsClient());
        AssertStatus(HttpStatusCode.OK, await laptop.LoginAsync("grace@example.org", Password));

        AssertStatus(HttpStatusCode.Accepted, await phone.PostAsync("/api/v1/auth/forgot-password", new { email = "grace@example.org", captchaToken = "t" }));
        var (userId, token) = api.Emails.LatestLink("grace@example.org", "password-reset", "reset-password");

        var weak = await phone.PostAsync("/api/v1/auth/reset-password", new { userId, token, newPassword = "short" });
        Assert.Equal("validation_failed", await ErrorCodeAsync(weak));
        Assert.Contains("password_too_short", (await JsonAsync(weak)).GetProperty("errors").GetProperty("newPassword").EnumerateArray().Select(e => e.GetString()));

        AssertStatus(HttpStatusCode.NoContent, await phone.PostAsync("/api/v1/auth/reset-password", new { userId, token, newPassword = NewPassword }));
        AssertStatus(HttpStatusCode.BadRequest, await phone.PostAsync("/api/v1/auth/reset-password", new { userId, token, newPassword = NewPassword + "!" }));

        // The laptop session was revoked server-side even though its cookie is still present.
        AssertStatus(HttpStatusCode.Unauthorized, await laptop.Client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative), Ct));
        Assert.Equal("invalid_credentials", await ErrorCodeAsync(await phone.LoginAsync("grace@example.org", Password)));
        AssertStatus(HttpStatusCode.OK, await phone.LoginAsync("grace@example.org", NewPassword));
    }

    [Fact]
    public async Task Change_password_keeps_current_session_and_revokes_others()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db);
        await CreateConfirmedUserAsync(api, "linus@example.org");
        using var current = new ApiSession(api.CreateHttpsClient());
        using var other = new ApiSession(api.CreateHttpsClient());
        await current.LoginAsync("linus@example.org", Password);
        await other.LoginAsync("linus@example.org", Password);

        var wrong = await current.PostAsync("/api/v1/users/me/password", new { currentPassword = "nope nope nope", newPassword = NewPassword });
        Assert.Contains("password_incorrect", (await JsonAsync(wrong)).GetProperty("errors").GetProperty("currentPassword").EnumerateArray().Select(e => e.GetString()));

        AssertStatus(HttpStatusCode.NoContent, await current.PostAsync("/api/v1/users/me/password", new { currentPassword = Password, newPassword = NewPassword }));

        AssertStatus(HttpStatusCode.OK, await current.Client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative), Ct));
        AssertStatus(HttpStatusCode.Unauthorized, await other.Client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative), Ct));
    }

    [Fact]
    public async Task Sessions_can_be_listed_and_revoked_only_by_their_owner()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db);
        await CreateConfirmedUserAsync(api, "a@example.org");
        await CreateConfirmedUserAsync(api, "b@example.org");
        using var first = new ApiSession(api.CreateHttpsClient());
        using var second = new ApiSession(api.CreateHttpsClient());
        using var stranger = new ApiSession(api.CreateHttpsClient());
        await first.LoginAsync("a@example.org", Password);
        await second.LoginAsync("a@example.org", Password, rememberMe: true);
        await stranger.LoginAsync("b@example.org", Password);

        var sessions = (await JsonAsync(await first.Client.GetAsync(new Uri("/api/v1/auth/sessions", UriKind.Relative), Ct))).EnumerateArray().ToList();
        Assert.Equal(2, sessions.Count);
        var otherId = sessions.Single(s => !s.GetProperty("current").GetBoolean()).GetProperty("id").GetGuid();

        Assert.Equal("session_not_found", await ErrorCodeAsync(await stranger.SendAsync(HttpMethod.Delete, $"/api/v1/auth/sessions/{otherId}")));
        AssertStatus(HttpStatusCode.OK, await second.Client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative), Ct));

        AssertStatus(HttpStatusCode.NoContent, await first.SendAsync(HttpMethod.Delete, $"/api/v1/auth/sessions/{otherId}"));
        AssertStatus(HttpStatusCode.Unauthorized, await second.Client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative), Ct));
        AssertStatus(HttpStatusCode.OK, await first.Client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative), Ct));
    }

    [Fact]
    public async Task State_changing_requests_require_the_antiforgery_token()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db);
        using var browser = new ApiSession(api.CreateHttpsClient());

        var response = await browser.PostAsync("/api/v1/auth/register",
            new { email = "x@example.org", password = Password, displayName = "X", captchaToken = "t" }, withCsrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("csrf_token_invalid", await ErrorCodeAsync(response));
        Assert.Empty(api.Emails.Sent);
    }

    [Fact]
    public async Task Registration_does_not_reveal_existing_accounts()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db);
        await CreateConfirmedUserAsync(api, "taken@example.org");
        using var browser = new ApiSession(api.CreateHttpsClient());

        var again = await browser.PostAsync("/api/v1/auth/register",
            new { email = "taken@example.org", password = "another password!", displayName = "Impostor", captchaToken = "t" });
        var unknownReset = await browser.PostAsync("/api/v1/auth/forgot-password", new { email = "nobody@example.org", captchaToken = "t" });

        AssertStatus(HttpStatusCode.Accepted, again);
        AssertStatus(HttpStatusCode.Accepted, unknownReset);
        Assert.Single(api.Emails.To("taken@example.org", "already-registered"));
        Assert.DoesNotContain(api.Emails.Sent, m => m.To == "nobody@example.org");
        // The original password still works: no account was overwritten.
        AssertStatus(HttpStatusCode.OK, await browser.LoginAsync("taken@example.org", Password));
    }

    [Fact]
    public async Task Failed_or_unavailable_captcha_blocks_registration_and_recovery()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db);
        using var browser = new ApiSession(api.CreateHttpsClient());
        var request = new { email = "bot@example.org", password = Password, displayName = "Bot", captchaToken = "t" };

        api.Captcha.Outcome = CaptchaOutcome.Failed;
        var failed = await browser.PostAsync("/api/v1/auth/register", request);
        api.Captcha.Outcome = CaptchaOutcome.Unavailable;
        var unavailable = await browser.PostAsync("/api/v1/auth/forgot-password", new { email = "bot@example.org", captchaToken = "t" });

        Assert.Equal((HttpStatusCode.BadRequest, "captcha_failed"), (failed.StatusCode, await ErrorCodeAsync(failed)));
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "captcha_unavailable"), (unavailable.StatusCode, await ErrorCodeAsync(unavailable)));
        await using var scope = api.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IUserAccountManager>().FindByEmailAsync("bot@example.org", Ct));
        Assert.Empty(api.Emails.Sent);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_without_revealing_it()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db, new() { ["Identity:MaxFailedAccessAttempts"] = "3" });
        await CreateConfirmedUserAsync(api, "locked@example.org");
        using var browser = new ApiSession(api.CreateHttpsClient());

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal("invalid_credentials", await ErrorCodeAsync(await browser.LoginAsync("locked@example.org", "wrong password " + i)));
        }

        // Even the correct password is refused while locked, with the same generic response.
        var locked = await browser.LoginAsync("locked@example.org", Password);
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_credentials"), (locked.StatusCode, await ErrorCodeAsync(locked)));
        Assert.Equal("invalid_credentials", await ErrorCodeAsync(await browser.LoginAsync("unknown@example.org", Password)));
    }

    [Fact]
    public async Task Permission_policy_requires_an_active_role()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db);
        var userId = await CreateConfirmedUserAsync(api, "admin@example.org");
        using var browser = new ApiSession(api.CreateHttpsClient());

        AssertStatus(HttpStatusCode.Unauthorized, await browser.Client.GetAsync(new Uri("/api/v1/admin/installation", UriKind.Relative), Ct));
        await browser.LoginAsync("admin@example.org", Password);
        var forbidden = await browser.Client.GetAsync(new Uri("/api/v1/admin/installation", UriKind.Relative), Ct);
        Assert.Equal((HttpStatusCode.Forbidden, "forbidden"), (forbidden.StatusCode, await ErrorCodeAsync(forbidden)));

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var granted = await scope.ServiceProvider.GetRequiredService<IUserAccountManager>().AddToRoleAsync(userId, "InstallationAdministrator", Ct);
            Assert.True(granted.Succeeded);
            var inactive = await scope.ServiceProvider.GetRequiredService<IUserAccountManager>().AddToRoleAsync(userId, "SupportAgent", Ct);
            Assert.False(inactive.Succeeded);
        }

        await browser.LoginAsync("admin@example.org", Password);
        var allowed = await browser.Client.GetAsync(new Uri("/api/v1/admin/installation", UriKind.Relative), Ct);
        AssertStatus(HttpStatusCode.OK, allowed);
        var me = await JsonAsync(await browser.Client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative), Ct));
        Assert.Contains("installation.read", me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task Configuration_reports_captcha_and_no_external_providers_and_disabled_registration_is_enforced()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db, new() { ["Identity:AllowRegistration"] = "false" });
        using var browser = new ApiSession(api.CreateHttpsClient());

        var config = await JsonAsync(await browser.Client.GetAsync(new Uri("/api/v1/auth/config", UriKind.Relative), Ct));
        var register = await browser.PostAsync("/api/v1/auth/register",
            new { email = "x@example.org", password = Password, displayName = "X", captchaToken = "t" });
        var google = await browser.Client.GetAsync(new Uri("/api/v1/auth/external/google", UriKind.Relative), Ct);

        Assert.False(config.GetProperty("registrationEnabled").GetBoolean());
        Assert.Equal("turnstile", config.GetProperty("captcha").GetProperty("provider").GetString());
        Assert.Equal("test-site-key", config.GetProperty("captcha").GetProperty("siteKey").GetString());
        Assert.Empty(config.GetProperty("externalProviders").EnumerateArray());
        Assert.Equal((HttpStatusCode.Forbidden, "registration_disabled"), (register.StatusCode, await ErrorCodeAsync(register)));
        Assert.Equal((HttpStatusCode.NotFound, "external_provider_not_enabled"), (google.StatusCode, await ErrorCodeAsync(google)));
    }

    [Fact]
    public async Task Enabled_external_provider_redirects_to_the_provider_with_the_api_callback()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db, new()
        {
            ["Authentication:Google:ClientId"] = "client-id.apps.googleusercontent.com",
            ["Authentication:Google:ClientSecret"] = "client-secret",
        });
        using var browser = new ApiSession(api.CreateHttpsClient());

        var config = await JsonAsync(await browser.Client.GetAsync(new Uri("/api/v1/auth/config", UriKind.Relative), Ct));
        var challenge = await browser.Client.GetAsync(new Uri("/api/v1/auth/external/google?returnUrl=//evil.example", UriKind.Relative), Ct);

        Assert.Equal(["google"], config.GetProperty("externalProviders").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var location = challenge.Headers.Location!.AbsoluteUri;
        Assert.StartsWith("https://accounts.google.com/", location, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("https://localhost/api/v1/auth/external/signin-google"), location, StringComparison.Ordinal);
        Assert.DoesNotContain("client-secret", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_provider_failure_is_reported_as_unavailable()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db);
        await CreateConfirmedUserAsync(api, "mail@example.org");
        using var browser = new ApiSession(api.CreateHttpsClient());
        api.Emails.Fail = true;

        var response = await browser.PostAsync("/api/v1/auth/forgot-password", new { email = "mail@example.org", captchaToken = "t" });

        Assert.Equal((HttpStatusCode.ServiceUnavailable, "email_unavailable"), (response.StatusCode, await ErrorCodeAsync(response)));
    }

    [Fact]
    public async Task Authentication_endpoints_are_rate_limited_per_client()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db, new() { ["RateLimiting:AuthPermitLimit"] = "3" });
        using var browser = new ApiSession(api.CreateHttpsClient());
        await browser.RefreshCsrfAsync();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await browser.PostAsync("/api/v1/auth/login", new { email = "x@example.org", password = "wrong password" })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
    }

    private static async Task<ChatXApiFactory> StartAsync(TestDatabase db, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = db.Provider,
            ["Database:ConnectionString"] = db.ConnectionString,
        };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        // Migrate before the host starts (its hosted services read the database at startup).
        await using (var standalone = ChatXServices.Build(db.Provider, db.ConnectionString))
        await using (var scope = standalone.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);
        }

        return new ChatXApiFactory(settings);
    }

    private static async Task<Guid> CreateConfirmedUserAsync(ChatXApiFactory api, string email)
    {
        using var browser = new ApiSession(api.CreateHttpsClient());
        var response = await browser.PostAsync("/api/v1/auth/register",
            new { email, password = Password, displayName = email.Split('@')[0], captchaToken = "t" });
        AssertStatus(HttpStatusCode.Accepted, response, await response.Content.ReadAsStringAsync(Ct));
        var (userId, token) = api.Emails.LatestLink(email, "email-confirmation", "verify-email");
        AssertStatus(HttpStatusCode.NoContent, await browser.PostAsync("/api/v1/auth/confirm-email", new { userId, token }));
        return userId;
    }
}
