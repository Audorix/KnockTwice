using System.Net;
using System.Text.Json;
using Audorix.ChatX.IntegrationTests.Support;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;
using static Audorix.ChatX.IntegrationTests.Support.TestUsers;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>
/// Reconnect catch-up (Phase 22, owner decision: up to 500 changes, otherwise reload): a client asks
/// what changed since its cursor and gets the current state of every entry it may see.
/// </summary>
public sealed class ChangeFeedTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Changes_since_a_cursor_are_returned_to_people_who_may_see_them(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, _) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        var messages = $"/api/v1/conversations/{direct}/messages";
        var old = (await JsonAsync(await ana.PostAsync(messages, new { clientMessageId = "a0", body = "Before" }))).GetProperty("id").GetGuid();

        // Ben's device takes a cursor, then goes offline while things happen.
        var benCursor = (await JsonAsync(await ben.GetAsync("/api/v1/changes/cursor"))).GetProperty("cursor").GetString()!;
        var carlCursor = (await JsonAsync(await carl.GetAsync("/api/v1/changes/cursor"))).GetProperty("cursor").GetString()!;
        var added = (await JsonAsync(await ana.PostAsync(messages, new { clientMessageId = "a1", body = "Lunch?" }))).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"{messages}/{added}", new { body = "Lunch at noon?" }));
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"{messages}/{old}/reactions", new { emoji = "👍", reacted = true }));

        var set = await JsonAsync(await ben.GetAsync($"/api/v1/changes?since={Uri.EscapeDataString(benCursor)}"));
        Assert.False(set.GetProperty("reset").GetBoolean());
        Assert.Equal(direct, Assert.Single(set.GetProperty("conversationIds").EnumerateArray()).GetGuid());
        var entries = set.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("entry")).ToList();
        var edited = entries.Single(e => e.GetProperty("id").GetGuid() == added);
        Assert.Equal("Lunch at noon?", edited.GetProperty("body").GetString());
        Assert.Equal(2, edited.GetProperty("version").GetInt32());
        var reacted = entries.Single(e => e.GetProperty("id").GetGuid() == old);
        Assert.Equal("👍", reacted.GetProperty("reactions")[0].GetProperty("emoji").GetString());

        // Asking again with the returned cursor is safe: recent changes may come again, never fewer.
        var again = await JsonAsync(await ben.GetAsync($"/api/v1/changes?since={Uri.EscapeDataString(set.GetProperty("cursor").GetString()!)}"));
        Assert.False(again.GetProperty("reset").GetBoolean());

        // Someone outside the conversation sees nothing of it; nonsense cursors are refused.
        var carlSet = await JsonAsync(await carl.GetAsync($"/api/v1/changes?since={Uri.EscapeDataString(carlCursor)}"));
        Assert.Empty(carlSet.GetProperty("entries").EnumerateArray());
        Assert.Empty(carlSet.GetProperty("conversationIds").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await ben.GetAsync("/api/v1/changes?since=nonsense")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task More_than_500_changes_ask_for_a_reload(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, _) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        var cursor = (await JsonAsync(await ben.GetAsync("/api/v1/changes/cursor"))).GetProperty("cursor").GetString()!;
        var carlCursor = (await JsonAsync(await carl.GetAsync("/api/v1/changes/cursor"))).GetProperty("cursor").GetString()!;
        for (var i = 0; i < 501; i++)
        {
            AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = $"m{i}", body = $"Message {i}" }));
        }

        var set = await JsonAsync(await ben.GetAsync($"/api/v1/changes?since={Uri.EscapeDataString(cursor)}"));
        Assert.True(set.GetProperty("reset").GetBoolean());
        Assert.Empty(set.GetProperty("entries").EnumerateArray());
        Assert.NotEqual(cursor, set.GetProperty("cursor").GetString());

        // From the new cursor nothing is pending.
        var after = await JsonAsync(await ben.GetAsync($"/api/v1/changes?since={Uri.EscapeDataString(set.GetProperty("cursor").GetString()!)}"));
        Assert.False(after.GetProperty("reset").GetBoolean());
        Assert.Empty(after.GetProperty("entries").EnumerateArray());

        // The limit counts the caller's own conversations: other people's busy chats never force a reload.
        var carlSet = await JsonAsync(await carl.GetAsync($"/api/v1/changes?since={Uri.EscapeDataString(carlCursor)}"));
        Assert.False(carlSet.GetProperty("reset").GetBoolean());
        Assert.Empty(carlSet.GetProperty("entries").EnumerateArray());
    }
}
