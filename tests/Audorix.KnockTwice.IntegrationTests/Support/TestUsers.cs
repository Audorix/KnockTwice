using System.Net;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;

namespace Audorix.KnockTwice.IntegrationTests.Support;

/// <summary>Starts a migrated API over a test database and creates confirmed, signed-in users.</summary>
internal static class TestUsers
{
    public const string Password = "correct horse battery";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<ChatXApiFactory> StartAsync(
        TestDatabase db, Dictionary<string, string?>? extra = null, Action<IServiceCollection>? services = null)
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

        // Migrate before the host starts: hosted services (Data Protection's key ring, the outbox
        // dispatcher) read the database at startup and would otherwise race the migration.
        await using (var standalone = ChatXServices.Build(db.Provider, db.ConnectionString))
        await using (var scope = standalone.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);
        }

        return new ChatXApiFactory(settings) { ExtraServices = services };
    }

    /// <summary>Registers, confirms and signs in a user; returns their browser session and id.</summary>
    public static async Task<(ApiSession Session, Guid Id)> SignedInAsync(ChatXApiFactory api, string email, string displayName)
    {
        var session = new ApiSession(api.CreateHttpsClient());
        var response = await session.PostAsync("/api/v1/auth/register",
            new { email, password = Password, displayName, captchaToken = "t" });
        AssertStatus(HttpStatusCode.Accepted, response, await response.Content.ReadAsStringAsync(Ct));
        var (userId, token) = api.Emails.LatestLink(email, "email-confirmation", "verify-email");
        AssertStatus(HttpStatusCode.NoContent, await session.PostAsync("/api/v1/auth/confirm-email", new { userId, token }));
        AssertStatus(HttpStatusCode.OK, await session.LoginAsync(email, Password));
        return (session, userId);
    }

    public static async Task<HttpResponseMessage> GetAsync(this ApiSession session, string path) =>
        await session.Client.GetAsync(new Uri(path, UriKind.Relative), Ct);

    /// <summary>Crossing contact requests make the two users contacts.</summary>
    public static async Task BecomeContactsAsync(ApiSession a, Guid aId, ApiSession b, Guid bId)
    {
        await a.PostAsync("/api/v1/contacts/requests", new { userId = bId });
        AssertStatus(HttpStatusCode.OK, await b.PostAsync("/api/v1/contacts/requests", new { userId = aId }));
    }

    /// <summary>Creates an organization owned by <paramref name="owner"/> and lets each member join by invitation.</summary>
    public static async Task<Guid> CreateOrganizationAsync(ApiSession owner, params (ApiSession Session, string Email)[] members)
    {
        var organization = await JsonAsync(await owner.PostAsync("/api/v1/organizations", new { name = "Acme" }));
        var organizationId = organization.GetProperty("id").GetGuid();
        foreach (var (session, email) in members)
        {
            var invite = await JsonAsync(await owner.PostAsync($"/api/v1/organizations/{organizationId}/invitations", new { email, role = "member" }));
            AssertStatus(HttpStatusCode.OK, await session.PostAsync($"/api/v1/organizations/invitations/{invite.GetProperty("id").GetGuid()}/accept"));
        }

        return organizationId;
    }
}
