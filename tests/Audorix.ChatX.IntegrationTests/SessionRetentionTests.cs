using System.Net;
using Audorix.ChatX.Application.Administration;
using Audorix.ChatX.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;
using static Audorix.ChatX.IntegrationTests.Support.TestUsers;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>Phase 21: ended sign-in sessions are purged by retention; active ones are kept.</summary>
public sealed class SessionRetentionTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Closed_sessions_are_purged_and_active_ones_kept(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, _) = await SignedInAsync(api, "ana@example.org", "Ana");
        using var _1 = ana;

        // A second browser signs in and out again.
        using var laptop = new ApiSession(api.CreateHttpsClient());
        AssertStatus(HttpStatusCode.OK, await laptop.LoginAsync("ana@example.org", Password));
        AssertStatus(HttpStatusCode.NoContent, await laptop.PostAsync("/api/v1/auth/logout"));

        // Nothing ended long enough ago yet.
        await using (var scope = api.Services.CreateAsyncScope())
        {
            Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<DataRetentionService>().ApplyAsync(DateTimeOffset.UtcNow, Ct)).Sessions);
        }

        // Thirty days (and an hour) later: the signed-out session goes, the active one stays.
        RetentionResult result;
        await using (var scope = api.Services.CreateAsyncScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<DataRetentionService>().ApplyAsync(DateTimeOffset.UtcNow.AddDays(30).AddHours(1), Ct);
        }

        Assert.Equal(1, result.Sessions);
        AssertStatus(HttpStatusCode.OK, await ana.GetAsync("/api/v1/users/me"));
        var sessions = await JsonAsync(await ana.GetAsync("/api/v1/auth/sessions"));
        Assert.Single(sessions.EnumerateArray());
    }
}
