using System.Net;
using System.Text.Json;
using Audorix.ChatX.IntegrationTests.Support;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;
using static Audorix.ChatX.IntegrationTests.Support.TestUsers;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>The conversation list a page at a time (Phase 22), in the same order as the full list.</summary>
public sealed class ConversationListPageTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Pages_cover_the_full_list_in_order(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        using var _ = ana;
        foreach (var name in new[] { "Ben", "Carl", "Dana" })
        {
            var (other, otherId) = await SignedInAsync(api, $"{name.ToLowerInvariant()}@example.org", name);
            using var __ = other;
            await BecomeContactsAsync(ana, anaId, other, otherId);
            AssertStatus(HttpStatusCode.Created, await ana.PostAsync("/api/v1/conversations/direct", new { userId = otherId }));
        }

        var full = (await JsonAsync(await ana.GetAsync("/api/v1/conversations"))).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(3, full.Count);

        var first = await JsonAsync(await ana.GetAsync("/api/v1/conversations/page?limit=2"));
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        var cursor = first.GetProperty("nextCursor").GetString()!;
        var second = await JsonAsync(await ana.GetAsync($"/api/v1/conversations/page?limit=2&after={Uri.EscapeDataString(cursor)}"));
        Assert.False(second.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);

        var paged = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray())
            .Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(full, paged);

        // Redesign 2026-11: the newest message is in the list (plain text), and pins are personal.
        var dana = full[0];
        var sent = await JsonAsync(await ana.PostAsync($"/api/v1/conversations/{dana}/messages", new { clientMessageId = "p1", body = "**Ready** for review" }));
        var listed = (await JsonAsync(await ana.GetAsync("/api/v1/conversations/page"))).GetProperty("items").EnumerateArray().First(c => c.GetProperty("id").GetGuid() == dana);
        var last = listed.GetProperty("lastMessage");
        Assert.Equal(("Ready for review", "Ana", false), (last.GetProperty("excerpt").GetString(), last.GetProperty("author").GetProperty("displayName").GetString(), last.GetProperty("deleted").GetBoolean()));
        Assert.False(listed.GetProperty("pinned").GetBoolean());

        // A deleted newest message is shown as deleted, without its text.
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete,$"/api/v1/conversations/{dana}/messages/{sent.GetProperty("id").GetGuid()}"));
        var afterDelete = (await JsonAsync(await ana.GetAsync("/api/v1/conversations/page"))).GetProperty("items").EnumerateArray()
            .First(c => c.GetProperty("id").GetGuid() == dana).GetProperty("lastMessage");
        Assert.Equal((true, JsonValueKind.Null), (afterDelete.GetProperty("deleted").GetBoolean(), afterDelete.GetProperty("excerpt").ValueKind));

        var pinned = await JsonAsync(await ana.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{full[2]}/pinned", new { pinned = true }));
        Assert.True(pinned.GetProperty("pinned").GetBoolean());
        var afterPin = (await JsonAsync(await ana.GetAsync("/api/v1/conversations"))).EnumerateArray().ToDictionary(c => c.GetProperty("id").GetGuid(), c => c.GetProperty("pinned").GetBoolean());
        Assert.Equal([false, false, true], full.Select(id => afterPin[id]));
        Assert.False((await JsonAsync(await ana.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{full[2]}/pinned", new { pinned = false }))).GetProperty("pinned").GetBoolean());

        // Shared files (redesign 2026-11): newest first, a page at a time; deleted messages' files are gone.
        var shared = full[1];
        var files = new List<Guid>();
        foreach (var (name, n) in new[] { ("plan.pdf", 1), ("chart.png", 2), ("notes.pdf", 3) })
        {
            var upload = await ana.UploadAsync($"/api/v1/conversations/{shared}/attachments", name, name.EndsWith(".png", StringComparison.Ordinal) ? TestFiles.Png(1024) : TestFiles.Pdf(),
                name.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "application/pdf");
            AssertStatus(HttpStatusCode.Created, upload, await upload.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            files.Add((await JsonAsync(upload)).GetProperty("id").GetGuid());
            AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{shared}/messages", new { clientMessageId = $"f{n}", body = "", attachmentIds = new[] { files[^1] } }));
        }

        var firstPage = await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{shared}/files?limit=2"));
        Assert.Equal(["notes.pdf", "chart.png"], firstPage.GetProperty("items").EnumerateArray().Select(f => f.GetProperty("file").GetProperty("fileName").GetString()));
        Assert.Equal("Ana", firstPage.GetProperty("items")[0].GetProperty("author").GetProperty("displayName").GetString());
        var older = await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{shared}/files?limit=2&before={Uri.EscapeDataString(firstPage.GetProperty("nextCursor").GetString()!)}"));
        Assert.Equal(["plan.pdf"], older.GetProperty("items").EnumerateArray().Select(f => f.GetProperty("file").GetProperty("fileName").GetString()));
        Assert.False(older.GetProperty("hasMore").GetBoolean());

        var newestMessage = firstPage.GetProperty("items")[0].GetProperty("messageId").GetGuid();
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{shared}/messages/{newestMessage}"));
        Assert.Equal(2, (await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{shared}/files"))).GetProperty("items").GetArrayLength());

        // The default page holds everything here; nonsense cursors are refused.
        Assert.Equal(3, (await JsonAsync(await ana.GetAsync("/api/v1/conversations/page"))).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.GetAsync("/api/v1/conversations/page?after=nonsense")).StatusCode);
    }
}
