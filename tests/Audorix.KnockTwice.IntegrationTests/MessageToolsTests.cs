using System.Net;
using System.Text.Json;
using Audorix.KnockTwice.IntegrationTests.Support;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// Message tools (Phase 18, owner decisions of 2026-11-23): pins (either person in direct
/// conversations, owners and administrators in groups and channels), saved messages that show only
/// while readable, and forwarding as a credited copy with its files.
/// </summary>
public sealed class MessageToolsTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Pins_follow_the_roles_and_show_in_history(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        await BecomeContactsAsync(ana, anaId, carl, carlId);

        // Direct conversation: either person pins.
        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        var hello = (await JsonAsync(await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "a1", body = "Wifi password is on the fridge" })))
            .GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{direct}/messages/{hello}/pin", new { pinned = true }));
        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{direct}/messages/{hello}/pin", new { pinned = true }));
        var pins = await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{direct}/pins"));
        var pin = Assert.Single(pins.EnumerateArray());
        Assert.Equal(hello, pin.GetProperty("entry").GetProperty("id").GetGuid());
        Assert.Equal(benId, pin.GetProperty("pinnedBy").GetProperty("id").GetGuid());

        // History: the message is marked and a "pinned" entry refers to it (recorded once).
        var history = (await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{direct}/history"))).GetProperty("items").EnumerateArray().ToList();
        Assert.True(history.Single(e => e.GetProperty("id").GetGuid() == hello).GetProperty("pinned").GetBoolean());
        var pinnedEntry = Assert.Single(history, e => e.GetProperty("kind").GetString() == "pinned");
        Assert.Equal(hello, pinnedEntry.GetProperty("replyTo").GetProperty("id").GetGuid());
        Assert.Equal(benId, pinnedEntry.GetProperty("actor").GetProperty("id").GetGuid());

        // Groups: only owners and administrators.
        var group = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Team", userIds = new[] { benId, carlId } }))).GetProperty("id").GetGuid();
        var plan = (await JsonAsync(await ben.PostAsync($"/api/v1/conversations/{group}/messages", new { clientMessageId = "b1", body = "The plan" }))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{group}/messages/{plan}/pin", new { pinned = true })).StatusCode);
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{group}/messages/{plan}/pin", new { pinned = true }));
        Assert.Single((await JsonAsync(await carl.GetAsync($"/api/v1/conversations/{group}/pins"))).EnumerateArray());

        // Unpinning and deleting the message remove the pin.
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{direct}/messages/{hello}/pin", new { pinned = false }));
        Assert.Empty((await JsonAsync(await ben.GetAsync($"/api/v1/conversations/{direct}/pins"))).EnumerateArray());
        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{group}/messages/{plan}"));
        Assert.Empty((await JsonAsync(await carl.GetAsync($"/api/v1/conversations/{group}/pins"))).EnumerateArray());

        // Outsiders cannot see pins.
        Assert.Equal(HttpStatusCode.NotFound, (await carl.GetAsync($"/api/v1/conversations/{direct}/pins")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Saved_messages_are_private_and_show_only_while_readable(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var group = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Team", userIds = new[] { benId } }))).GetProperty("id").GetGuid();
        var messages = $"/api/v1/conversations/{group}/messages";
        var first = (await JsonAsync(await ana.PostAsync(messages, new { clientMessageId = "a1", body = "Deadline is Friday" }))).GetProperty("id").GetGuid();
        var second = (await JsonAsync(await ana.PostAsync(messages, new { clientMessageId = "a2", body = "Budget attached" }))).GetProperty("id").GetGuid();

        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Put, $"{messages}/{first}/saved", new { saved = true }));
        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Put, $"{messages}/{second}/saved", new { saved = true }));
        var saved = await JsonAsync(await ben.GetAsync("/api/v1/saved"));
        Assert.Equal([second, first], saved.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("entry").GetProperty("id").GetGuid()).ToList());
        Assert.Equal(group, saved.GetProperty("items")[0].GetProperty("conversationId").GetGuid());

        // Only Ben sees his bookmarks.
        var bens = (await JsonAsync(await ben.GetAsync($"/api/v1/conversations/{group}/history"))).GetProperty("items").EnumerateArray()
            .Single(e => e.GetProperty("id").GetGuid() == first);
        Assert.True(bens.GetProperty("saved").GetBoolean());
        var anas = (await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{group}/history"))).GetProperty("items").EnumerateArray()
            .Single(e => e.GetProperty("id").GetGuid() == first);
        Assert.False(anas.GetProperty("saved").GetBoolean());
        Assert.Empty((await JsonAsync(await ana.GetAsync("/api/v1/saved"))).GetProperty("items").EnumerateArray());

        // Paging.
        var page = await JsonAsync(await ben.GetAsync("/api/v1/saved?limit=1"));
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        var older = await JsonAsync(await ben.GetAsync($"/api/v1/saved?limit=1&before={Uri.EscapeDataString(page.GetProperty("nextCursor").GetString()!)}"));
        Assert.Equal(first, older.GetProperty("items")[0].GetProperty("entry").GetProperty("id").GetGuid());

        // Unsaving, deletion and leaving all take messages off the list.
        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Put, $"{messages}/{second}/saved", new { saved = false }));
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"{messages}/{first}"));
        Assert.Empty((await JsonAsync(await ben.GetAsync("/api/v1/saved"))).GetProperty("items").EnumerateArray());
        var third = (await JsonAsync(await ana.PostAsync(messages, new { clientMessageId = "a3", body = "Still here" }))).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Put, $"{messages}/{third}/saved", new { saved = true }));
        AssertStatus(HttpStatusCode.NoContent, await ben.PostAsync($"/api/v1/conversations/{group}/leave"));
        Assert.Empty((await JsonAsync(await ben.GetAsync("/api/v1/saved"))).GetProperty("items").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Forwarding_copies_text_and_files_with_credit(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        await BecomeContactsAsync(ana, anaId, carl, carlId);
        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        var withCarl = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = carlId }))).GetProperty("id").GetGuid();

        // Ben sends a photo with text to Ana.
        var upload = await JsonAsync(await ben.UploadAsync($"/api/v1/conversations/{direct}/attachments", "view.png", TestFiles.Png(2048), "image/png"));
        var original = await JsonAsync(await ben.PostAsync($"/api/v1/conversations/{direct}/messages",
            new { clientMessageId = "b1", body = "Look at this view", attachmentIds = new[] { upload.GetProperty("id").GetGuid() } }));
        var originalId = original.GetProperty("id").GetGuid();

        // Ana forwards it to Carl with a comment.
        var forward = $"/api/v1/conversations/{direct}/messages/{originalId}/forward";
        var created = await ana.PostAsync(forward, new { targetConversationId = withCarl, clientMessageId = "f1", comment = "From Ben:" });
        AssertStatus(HttpStatusCode.Created, created, await created.Content.ReadAsStringAsync(Ct));
        var copy = await JsonAsync(created);
        Assert.Equal("Look at this view", copy.GetProperty("body").GetString());
        Assert.Equal(anaId, copy.GetProperty("actor").GetProperty("id").GetGuid());
        Assert.Equal(benId, copy.GetProperty("forwardedFrom").GetProperty("id").GetGuid());
        var file = copy.GetProperty("attachments")[0];
        Assert.NotEqual(upload.GetProperty("id").GetGuid(), file.GetProperty("id").GetGuid());
        Assert.Equal("view.png", file.GetProperty("fileName").GetString());

        // Carl sees the comment then the copy, and can open the copied file (not the original).
        var carlHistory = (await JsonAsync(await carl.GetAsync($"/api/v1/conversations/{withCarl}/history"))).GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("kind").GetString() == "message").Select(e => e.GetProperty("body").GetString()).ToList();
        Assert.Equal(["From Ben:", "Look at this view"], carlHistory);
        var content = await carl.GetAsync($"/api/v1/attachments/{file.GetProperty("id").GetGuid()}/content");
        AssertStatus(HttpStatusCode.OK, content);
        Assert.Equal(2048, (await content.Content.ReadAsByteArrayAsync(Ct)).Length);
        Assert.Equal(HttpStatusCode.NotFound, (await carl.GetAsync($"/api/v1/attachments/{upload.GetProperty("id").GetGuid()}/content")).StatusCode);

        // A retry returns the same copy; forwarding into a conversation you are not in is refused.
        var retry = await ana.PostAsync(forward, new { targetConversationId = withCarl, clientMessageId = "f1", comment = "From Ben:" });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(copy.GetProperty("id").GetGuid(), (await JsonAsync(retry)).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await carl.PostAsync($"/api/v1/conversations/{withCarl}/messages/{copy.GetProperty("id").GetGuid()}/forward",
            new { targetConversationId = direct, clientMessageId = "c1" })).StatusCode);
    }
}
