using System.Net;
using Audorix.KnockTwice.IntegrationTests.Support;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>Phase 8 preferences: theme and desktop notifications follow the user across devices.</summary>
public sealed class PreferencesTests
{
    [Fact]
    public async Task Theme_and_notification_preferences_are_saved_independently()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (ana, _) = await SignedInAsync(api, "ana@example.org", "Ana");
        using var _1 = ana;

        var initial = (await JsonAsync(await ana.GetAsync("/api/v1/users/me"))).GetProperty("preferences");
        Assert.Equal(("en", "system", false, true), (initial.GetProperty("uiLanguage").GetString(), initial.GetProperty("theme").GetString(),
            initial.GetProperty("desktopNotifications").GetBoolean(), initial.GetProperty("notificationPreviews").GetBoolean()));

        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, "/api/v1/users/me/preferences", new { theme = "dark", desktopNotifications = true }));
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, "/api/v1/users/me/preferences", new { uiLanguage = "es" }));
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, "/api/v1/users/me/preferences", new { notificationPreviews = false }));

        // Another device sees the same preferences.
        using var other = new ApiSession(api.CreateHttpsClient());
        AssertStatus(HttpStatusCode.OK, await other.LoginAsync("ana@example.org", Password));
        var saved = (await JsonAsync(await other.GetAsync("/api/v1/users/me"))).GetProperty("preferences");
        Assert.Equal(("es", "dark", true, false), (saved.GetProperty("uiLanguage").GetString(), saved.GetProperty("theme").GetString(),
            saved.GetProperty("desktopNotifications").GetBoolean(), saved.GetProperty("notificationPreviews").GetBoolean()));

        var invalid = await ana.SendAsync(HttpMethod.Put, "/api/v1/users/me/preferences", new { theme = "neon" });
        Assert.Equal("theme_invalid", (await JsonAsync(invalid)).GetProperty("errors").GetProperty("theme")[0].GetString());

        // An empty request is still refused as before.
        var empty = await ana.SendAsync(HttpMethod.Put, "/api/v1/users/me/preferences", new { });
        Assert.Equal("unsupported_language", (await JsonAsync(empty)).GetProperty("errors").GetProperty("uiLanguage")[0].GetString());
    }
}
