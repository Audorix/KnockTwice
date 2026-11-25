using System.Net;
using System.Text.Json;
using Audorix.KnockTwice.IntegrationTests.Support;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>Display ID and email sharing for the contact drawer (owner decisions 2026-11-24).</summary>
public sealed class ContactDetailsTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Display_ids_are_made_from_names_unique_and_editable_and_emails_are_shown_only_when_shared(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Aarón Medina");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Aaron Medina");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;

        // Made from the name (accents dropped), unique: the second gets a number.
        Assert.Equal("aaronmedina", (await JsonAsync(await ana.GetAsync("/api/v1/users/me"))).GetProperty("handle").GetString());
        Assert.Equal("aaronmedina2", (await JsonAsync(await ben.GetAsync("/api/v1/users/me"))).GetProperty("handle").GetString());

        // Only contacts see the details; the email only when shared (off by default).
        Assert.Equal(HttpStatusCode.NotFound, (await carl.GetAsync($"/api/v1/contacts/{anaId}/details")).StatusCode);
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var hidden = await JsonAsync(await ben.GetAsync($"/api/v1/contacts/{anaId}/details"));
        Assert.Equal("aaronmedina", hidden.GetProperty("handle").GetString());
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("email").ValueKind);
        Assert.False(hidden.GetProperty("emailShared").GetBoolean());

        var shared = await JsonAsync(await ana.SendAsync(HttpMethod.Put, "/api/v1/users/me/preferences", new { shareEmailWithContacts = true }));
        Assert.True(shared.GetProperty("preferences").GetProperty("shareEmailWithContacts").GetBoolean());
        var visible = await JsonAsync(await ben.GetAsync($"/api/v1/contacts/{anaId}/details"));
        Assert.Equal("ana@example.org", visible.GetProperty("email").GetString());
        Assert.True(visible.GetProperty("emailShared").GetBoolean());

        // Editable: a leading @ is ignored, case is folded; taken and malformed IDs are refused.
        var renamed = await JsonAsync(await ana.SendAsync(HttpMethod.Put, "/api/v1/users/me", new { displayName = "Aarón Medina", handle = "@Aaron.M" }));
        Assert.Equal("aaron.m", renamed.GetProperty("handle").GetString());
        var taken = await ben.SendAsync(HttpMethod.Put, "/api/v1/users/me", new { displayName = "Aaron Medina", handle = "aaron.m" });
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Contains("handle_taken", await taken.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var invalid = await ben.SendAsync(HttpMethod.Put, "/api/v1/users/me", new { displayName = "Aaron Medina", handle = "a b" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("handle_invalid", await invalid.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal("aaronmedina2", (await JsonAsync(await ben.GetAsync("/api/v1/users/me"))).GetProperty("handle").GetString());

        // The freed ID can be claimed by someone else.
        var claimed = await JsonAsync(await ben.SendAsync(HttpMethod.Put, "/api/v1/users/me", new { displayName = "Aaron Medina", handle = "aaronmedina" }));
        Assert.Equal("aaronmedina", claimed.GetProperty("handle").GetString());
        _ = carlId;
    }
}
