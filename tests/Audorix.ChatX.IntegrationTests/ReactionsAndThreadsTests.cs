using System.Net;
using System.Text.Json;
using Audorix.ChatX.IntegrationTests.Support;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;
using static Audorix.ChatX.IntegrationTests.Support.TestUsers;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>
/// Reactions and threads (Phase 16, owner decisions of 2026-11-23): any emoji, at most 20 different
/// ones per message; threads in a side panel with an optional echo to the conversation, their own
/// unread state and followers.
/// </summary>
public sealed class ReactionsAndThreadsTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Reactions_toggle_per_person_and_are_limited(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var conversation = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        var message = (await JsonAsync(await ana.PostAsync($"/api/v1/conversations/{conversation}/messages", new { clientMessageId = "m1", body = "Hello" })))
            .GetProperty("id").GetGuid();
        var url = $"/api/v1/conversations/{conversation}/messages/{message}/reactions";

        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, url, new { emoji = "👍", reacted = true }));
        // Adding the same reaction twice is harmless.
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, url, new { emoji = "👍", reacted = true }));
        var both = await JsonAsync(await ben.SendAsync(HttpMethod.Put, url, new { emoji = "👍🏽", reacted = true }));
        Assert.Equal(2, both.GetArrayLength());
        Assert.Equal("👍", both[0].GetProperty("emoji").GetString());
        Assert.Equal(1, both[0].GetProperty("count").GetInt32());
        Assert.Equal(anaId, both[0].GetProperty("userIds")[0].GetGuid());

        // History shows the reactions; removing your own takes it away.
        var history = await JsonAsync(await ben.GetAsync($"/api/v1/conversations/{conversation}/history"));
        var entry = history.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == message);
        Assert.Equal(2, entry.GetProperty("reactions").GetArrayLength());
        var after = await JsonAsync(await ana.SendAsync(HttpMethod.Put, url, new { emoji = "👍", reacted = false }));
        Assert.Equal("👍🏽", Assert.Single(after.EnumerateArray()).GetProperty("emoji").GetString());

        // Text, several emoji and empty values are not reactions.
        foreach (var invalid in new[] { "ok", "👍👍", "", " ", "1" })
        {
            var response = await ana.SendAsync(HttpMethod.Put, url, new { emoji = invalid, reacted = true });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // At most 20 different emoji on one message.
        var emoji = new[] { "😀", "😁", "😂", "🤣", "😃", "😄", "😅", "😆", "😉", "😊", "😋", "😎", "😍", "😘", "🥰", "😗", "😙", "😚", "🙂" };
        foreach (var e in emoji)
        {
            AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, url, new { emoji = e, reacted = true }));
        }

        var tooMany = await ben.SendAsync(HttpMethod.Put, url, new { emoji = "🤗", reacted = true });
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);
        Assert.Contains("too_many_reactions", await tooMany.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        // An emoji already on the message can still be added by someone else.
        AssertStatus(HttpStatusCode.OK, await ben.SendAsync(HttpMethod.Put, url, new { emoji = "😀", reacted = true }));

        // Someone outside the conversation cannot react.
        var (carl, _) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _3 = carl;
        Assert.Equal(HttpStatusCode.NotFound, (await carl.SendAsync(HttpMethod.Put, url, new { emoji = "👍", reacted = true })).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Thread_replies_stay_out_of_the_conversation_unless_echoed(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var conversation = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        var messages = $"/api/v1/conversations/{conversation}/messages";
        var root = (await JsonAsync(await ana.PostAsync(messages, new { clientMessageId = "root", body = "Plan for Friday?" }))).GetProperty("id").GetGuid();

        var hidden = await JsonAsync(await ben.PostAsync(messages, new { clientMessageId = "r1", body = "Hike", threadRootId = root }));
        Assert.Equal(root, hidden.GetProperty("threadRootId").GetGuid());
        Assert.False(hidden.GetProperty("shownInConversation").GetBoolean());
        var echoed = await JsonAsync(await ben.PostAsync(messages, new { clientMessageId = "r2", body = "Or a movie", threadRootId = root, alsoInConversation = true }));
        Assert.True(echoed.GetProperty("shownInConversation").GetBoolean());

        // Replies to a reply are not threads of their own.
        var nested = await ben.PostAsync(messages, new { clientMessageId = "r3", body = "x", threadRootId = hidden.GetProperty("id").GetGuid() });
        Assert.Equal(HttpStatusCode.NotFound, nested.StatusCode);

        // The conversation shows the first message (with its thread summary) and the echoed reply only.
        var history = await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{conversation}/history"));
        var bodies = history.GetProperty("items").EnumerateArray().Where(e => e.GetProperty("kind").GetString() == "message")
            .Select(e => e.GetProperty("body").GetString()).ToList();
        Assert.Equal(["Plan for Friday?", "Or a movie"], bodies);
        var rootEntry = history.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == root);
        var summary = rootEntry.GetProperty("thread");
        Assert.Equal(2, summary.GetProperty("replyCount").GetInt32());
        Assert.True(summary.GetProperty("following").GetBoolean());
        Assert.Equal(2, summary.GetProperty("unread").GetInt32());

        // The conversation's unread count does not include the hidden reply.
        var list = await JsonAsync(await ana.GetAsync("/api/v1/conversations"));
        Assert.Equal(1, list.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == conversation).GetProperty("unreadCount").GetInt32());

        // The thread lists both replies in order; reading it clears its unread count.
        var thread = await JsonAsync(await ana.GetAsync($"{messages}/{root}/thread"));
        Assert.Equal(root, thread.GetProperty("root").GetProperty("id").GetGuid());
        Assert.Equal(["Hike", "Or a movie"], thread.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("body").GetString()).ToList());
        var last = thread.GetProperty("items")[1].GetProperty("sequence").GetInt64();
        var read = await JsonAsync(await ana.PostAsync($"{messages}/{root}/thread/read", new { sequence = last }));
        Assert.Equal(0, read.GetProperty("unread").GetInt32());

        // Deleting a reply lowers the count; unfollowing is remembered.
        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Delete, $"{messages}/{hidden.GetProperty("id").GetGuid()}"));
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"{messages}/{root}/thread/follow", new { following = false }));
        var again = await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{conversation}/history"));
        var rootAgain = again.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == root).GetProperty("thread");
        Assert.Equal(1, rootAgain.GetProperty("replyCount").GetInt32());
        Assert.False(rootAgain.GetProperty("following").GetBoolean());
        Assert.False((await JsonAsync(await ana.GetAsync($"{messages}/{root}/thread"))).GetProperty("following").GetBoolean());
    }
}
