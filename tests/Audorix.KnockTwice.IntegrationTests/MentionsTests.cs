using System.Net;
using System.Text.Json;
using Audorix.KnockTwice.IntegrationTests.Support;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// Mentions and notification levels (Phase 17, owner decisions of 2026-11-23): named mentions of
/// participants; @everyone / @here for anyone in rooms up to the limit, otherwise owners and
/// administrators; levels all / mentions / nothing (mentions break through "mentions", not
/// "nothing"); the Mentions view with unread state.
/// </summary>
public sealed class MentionsTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Mentions_are_validated_listed_and_read(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db, new Dictionary<string, string?> { ["Messaging:EveryoneMentionLimit"] = "2" });
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        var (dana, danaId) = await SignedInAsync(api, "dana@example.org", "Dana");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        using var _4 = dana;
        foreach (var (other, otherId) in new[] { (ben, benId), (carl, carlId), (dana, danaId) })
        {
            await BecomeContactsAsync(ana, anaId, other, otherId);
        }

        var group = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Team", userIds = new[] { benId, carlId } }))).GetProperty("id").GetGuid();
        var messages = $"/api/v1/conversations/{group}/messages";

        // Ben mentions Ana by name: it shows on the message and in Ana's mentions, unread.
        var hello = await JsonAsync(await ben.PostAsync(messages, new { clientMessageId = "b1", body = "Hi @Ana, can you check?", mentionUserIds = new[] { anaId } }));
        Assert.Equal(anaId, Assert.Single(hello.GetProperty("mentions").GetProperty("userIds").EnumerateArray()).GetGuid());
        Assert.False(hello.GetProperty("mentions").GetProperty("everyone").GetBoolean());
        var mine = await JsonAsync(await ana.GetAsync("/api/v1/mentions"));
        var item = Assert.Single(mine.GetProperty("items").EnumerateArray());
        Assert.Equal(group, item.GetProperty("conversationId").GetGuid());
        Assert.Equal("group", item.GetProperty("conversationType").GetString());
        Assert.True(item.GetProperty("unread").GetBoolean());
        Assert.Equal(1, (await JsonAsync(await ana.GetAsync("/api/v1/mentions/unread"))).GetProperty("unread").GetInt32());
        Assert.Empty((await JsonAsync(await carl.GetAsync("/api/v1/mentions"))).GetProperty("items").EnumerateArray());

        // Reading the conversation reads the mention.
        AssertStatus(HttpStatusCode.OK, await ana.PostAsync($"/api/v1/conversations/{group}/read", new { sequence = hello.GetProperty("sequence").GetInt64() }));
        Assert.Equal(0, (await JsonAsync(await ana.GetAsync("/api/v1/mentions/unread"))).GetProperty("unread").GetInt32());
        Assert.False((await JsonAsync(await ana.GetAsync("/api/v1/mentions"))).GetProperty("items")[0].GetProperty("unread").GetBoolean());

        // Only participants can be mentioned.
        var outsider = await ben.PostAsync(messages, new { clientMessageId = "b2", body = "@Dana", mentionUserIds = new[] { danaId } });
        Assert.Equal(HttpStatusCode.BadRequest, outsider.StatusCode);
        Assert.Contains("mention_invalid", await outsider.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Above the limit (2 here) only owners and administrators may notify everyone.
        var forbidden = await ben.PostAsync(messages, new { clientMessageId = "b3", body = "@everyone lunch?" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Contains("mention_everyone_forbidden", await forbidden.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var everyone = await JsonAsync(await ana.PostAsync(messages, new { clientMessageId = "a1", body = "@everyone lunch at noon", mentionUserIds = new[] { benId } }));
        Assert.True(everyone.GetProperty("mentions").GetProperty("everyone").GetBoolean());
        Assert.Equal(benId, Assert.Single(everyone.GetProperty("mentions").GetProperty("userIds").EnumerateArray()).GetGuid());
        foreach (var person in new[] { ben, carl })
        {
            Assert.Equal(1, (await JsonAsync(await person.GetAsync("/api/v1/mentions/unread"))).GetProperty("unread").GetInt32());
        }

        // "@everyone" inside a word is not a mention; neither is your own name.
        var plain = await JsonAsync(await ana.PostAsync(messages, new { clientMessageId = "a2", body = "mail me at x@everyone.example", mentionUserIds = new[] { anaId } }));
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("mentions").ValueKind);

        // Deleting the message removes its mentions.
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"{messages}/{everyone.GetProperty("id").GetGuid()}"));
        Assert.Empty((await JsonAsync(await carl.GetAsync("/api/v1/mentions"))).GetProperty("items").EnumerateArray());

        // A member who leaves no longer sees mentions from there.
        AssertStatus(HttpStatusCode.NoContent, await ana.PostAsync($"/api/v1/conversations/{group}/leave"));
        Assert.Empty((await JsonAsync(await ana.GetAsync("/api/v1/mentions"))).GetProperty("items").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Notification_levels_decide_what_is_emailed(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db, new Dictionary<string, string?>
        {
            ["Notifications:DispatchInterval"] = "00:00:00.200",
            ["Notifications:EmailDelay"] = "00:00:00",
            ["Notifications:EmailInterval"] = "01:00:00",
        });
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        await BecomeContactsAsync(ana, anaId, carl, carlId);
        var group = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Team", userIds = new[] { benId, carlId } }))).GetProperty("id").GetGuid();
        var settings = $"/api/v1/conversations/{group}/settings";

        // Ben: only mentions (the old "muted: true" means the same); Carl: nothing at all.
        var benLevel = await JsonAsync(await ben.SendAsync(HttpMethod.Put, settings, new { muted = true }));
        Assert.Equal("mentions", benLevel.GetProperty("notificationLevel").GetString());
        Assert.True(benLevel.GetProperty("muted").GetBoolean());
        Assert.Equal("nothing", (await JsonAsync(await carl.SendAsync(HttpMethod.Put, settings, new { notificationLevel = "nothing" }))).GetProperty("notificationLevel").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await carl.SendAsync(HttpMethod.Put, settings, new { notificationLevel = "loud" })).StatusCode);

        var messages = $"/api/v1/conversations/{group}/messages";
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync(messages, new { clientMessageId = "a1", body = "Unrelated chatter" }));
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync(messages, new { clientMessageId = "a2", body = "@Ben @Carl please review", mentionUserIds = new[] { benId, carlId } }));

        Application.Abstractions.EmailMessage? benEmail = null;
        for (var i = 0; i < 100 && benEmail is null; i++)
        {
            benEmail = api.Emails.To("ben@example.org", "unread-messages") is [var first, ..] ? first : null;
            await Task.Delay(100, Ct);
        }

        Assert.NotNull(benEmail);
        Assert.Contains("please review", benEmail.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Unrelated", benEmail.TextBody, StringComparison.Ordinal);
        await Task.Delay(1000, Ct);
        Assert.Empty(api.Emails.To("carl@example.org", "unread-messages"));
    }
}
