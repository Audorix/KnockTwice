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

        // The default page holds everything here; nonsense cursors are refused.
        Assert.Equal(3, (await JsonAsync(await ana.GetAsync("/api/v1/conversations/page"))).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.GetAsync("/api/v1/conversations/page?after=nonsense")).StatusCode);
    }
}
