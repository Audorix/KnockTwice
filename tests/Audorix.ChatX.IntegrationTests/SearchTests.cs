using System.Net;
using System.Text.Json;
using Audorix.ChatX.Domain.Authorization;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;
using static Audorix.ChatX.IntegrationTests.Support.TestUsers;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>
/// Message search with the default database provider (Phase 14): SQLite FTS5 and PostgreSQL
/// full-text. Every word matches as a prefix, accent- and case-blind; results respect access and
/// history-from-join; edits and deletions reach the index through the queue.
/// </summary>
public sealed class SearchTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, string?> FastIndexing = new() { ["Search:IndexInterval"] = "00:00:00.100" };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Search_finds_what_each_person_may_read(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db, new(FastIndexing));
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        await BecomeContactsAsync(ana, anaId, carl, carlId);

        var withBen = await DirectAsync(ana, benId);
        var withCarl = await DirectAsync(ana, carlId);
        var budget = await SendAsync(ana, withBen, "b1", "El presupuesto del Café para 2026 está listo");
        await SendAsync(ana, withCarl, "c1", "Presupuesto secreto");
        var upload = await ana.UploadAsync($"/api/v1/conversations/{withBen}/attachments", "Factura-Marzo.png", TestFiles.Png(1024), "image/png");
        AssertStatus(HttpStatusCode.Created, upload, await upload.Content.ReadAsStringAsync(Ct));
        var invoice = await SendAsync(ana, withBen, "b2", "Adjunto", (await JsonAsync(upload)).GetProperty("id").GetGuid());

        // A group Carl joins later: he must not find what was said before his join.
        var group = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Proyecto", userIds = new[] { benId } }))).GetProperty("id").GetGuid();
        await SendAsync(ana, group, "g1", "plan antiguo");
        AssertStatus(HttpStatusCode.OK, await ana.PostAsync($"/api/v1/conversations/{group}/participants", new { userIds = new[] { carlId } }));
        var later = await SendAsync(ana, group, "g2", "plan nuevo");

        // Prefix, accent- and case-blind; only Ben's own conversations.
        var found = await WaitForAsync(ben, "presup CAFE", 1);
        Assert.Equal(budget, found.Single().Message);
        Assert.Equal(withBen, found.Single().Conversation);
        Assert.Empty(await SearchAsync(ben, "secreto"));
        Assert.Single(await SearchAsync(carl, "secreto"));

        // History from the join.
        var plans = await WaitForAsync(carl, "plan", 1);
        Assert.Equal([later], plans.Select(p => p.Message));
        Assert.Equal(2, (await SearchAsync(ben, "plan")).Count);

        // Attachment file names, and the filters.
        Assert.Equal([invoice], (await WaitForAsync(ben, "factura marzo", 1)).Select(r => r.Message));
        Assert.Equal([invoice], (await SearchAsync(ben, "adjunto", "&hasFiles=true")).Select(r => r.Message));
        Assert.Empty(await SearchAsync(ben, "adjunto", "&hasFiles=false"));
        Assert.Empty(await SearchAsync(ben, "presupuesto", $"&senderId={benId}"));
        Assert.Single(await SearchAsync(ana, "presupuesto", $"&conversationId={withCarl}"));
        Assert.Empty(await SearchAsync(ana, "presupuesto", $"&from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}"));

        // Paging: newest first, then the next page.
        var first = await JsonAsync(await ana.GetAsync("/api/v1/search?q=presupuesto&limit=1"));
        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);
        var second = await JsonAsync(await ana.GetAsync($"/api/v1/search?q=presupuesto&limit=1&cursor={Uri.EscapeDataString(cursor)}"));
        Assert.NotEqual(Ids(first).Single(), Ids(second).Single());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);

        // Edits and deletions reach the index.
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{withBen}/messages/{budget}", new { body = "La cotización está lista" }));
        Assert.Equal([budget], (await WaitForAsync(ben, "cotizacion", 1)).Select(r => r.Message));
        await WaitForCountAsync(ben, "presupuesto", 0);
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{withBen}/messages/{budget}"));
        await WaitForCountAsync(ben, "cotizacion", 0);

        // Leaving a group ends search access at once.
        AssertStatus(HttpStatusCode.NoContent, await carl.PostAsync($"/api/v1/conversations/{group}/leave"));
        Assert.Empty(await SearchAsync(carl, "plan"));

        // Validation.
        Assert.Equal(HttpStatusCode.BadRequest, (await ben.GetAsync("/api/v1/search?q=a")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ben.GetAsync("/api/v1/search?q=plan&cursor=nonsense")).StatusCode);
        using var anonymous = api.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/search?q=plan", Ct)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Search_counts_matches_orders_oldest_first_and_shows_a_message_in_context(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db, new(FastIndexing));
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        await BecomeContactsAsync(ana, anaId, ben, benId);

        var direct = await DirectAsync(ana, benId);
        var first = await SendAsync(ana, direct, "r1", "Release one");
        var chat = await SendAsync(ben, direct, "x1", "Lunch?");
        var upload = await ana.UploadAsync($"/api/v1/conversations/{direct}/attachments", "notes.png", TestFiles.Png(1024), "image/png");
        AssertStatus(HttpStatusCode.Created, upload, await upload.Content.ReadAsStringAsync(Ct));
        var second = await SendAsync(ana, direct, "r2", "Release two", (await JsonAsync(upload)).GetProperty("id").GetGuid());
        var third = await SendAsync(ben, direct, "r3", "Release three");
        await WaitForAsync(ben, "release", 3);

        // Counts on the first page, the same whatever the files filter; none on later pages.
        var page = await JsonAsync(await ben.GetAsync("/api/v1/search?q=release&limit=2&hasFiles=false"));
        Assert.Equal(3, page.GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("counts").GetProperty("withFiles").GetInt32());
        Assert.Equal([third, first], Ids(page));
        var cursor = page.GetProperty("nextCursor").GetString();
        Assert.Null(cursor); // only two without files
        var narrowed = await JsonAsync(await ben.GetAsync($"/api/v1/search?q=release&senderId={benId}"));
        Assert.Equal(1, narrowed.GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(0, narrowed.GetProperty("counts").GetProperty("withFiles").GetInt32());

        // Oldest first, paged.
        var oldest = await JsonAsync(await ben.GetAsync("/api/v1/search?q=release&limit=2&order=oldest"));
        Assert.Equal([first, second], Ids(oldest));
        var next = oldest.GetProperty("nextCursor").GetString()!;
        var rest = await JsonAsync(await ben.GetAsync($"/api/v1/search?q=release&limit=2&order=oldest&cursor={Uri.EscapeDataString(next)}"));
        Assert.Equal([third], Ids(rest));
        Assert.Equal(JsonValueKind.Null, rest.GetProperty("counts").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await ben.GetAsync("/api/v1/search?q=release&order=sideways")).StatusCode);

        // Results carry the caller's saved state.
        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{direct}/messages/{first}/saved", new { saved = true }));
        var withSaved = await JsonAsync(await ben.GetAsync("/api/v1/search?q=release&order=oldest&limit=1"));
        Assert.True(withSaved.GetProperty("items")[0].GetProperty("message").GetProperty("saved").GetBoolean());

        // A message with its neighbours.
        var context = await JsonAsync(await ben.GetAsync($"/api/v1/conversations/{direct}/messages/{second}/context?size=1"));
        Assert.Equal([chat, second, third], context.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        Assert.True(context.GetProperty("hasOlder").GetBoolean());
        Assert.False(context.GetProperty("hasNewer").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await carl.GetAsync($"/api/v1/conversations/{direct}/messages/{chat}/context")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ben.GetAsync($"/api/v1/conversations/{direct}/messages/{chat}/context?size=21")).StatusCode);

        // In a group joined later, nothing from before the join.
        var group = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Team", userIds = new[] { benId } }))).GetProperty("id").GetGuid();
        var old = await SendAsync(ana, group, "g1", "Before Carl");
        await BecomeContactsAsync(ana, anaId, carl, carlId);
        AssertStatus(HttpStatusCode.OK, await ana.PostAsync($"/api/v1/conversations/{group}/participants", new { userIds = new[] { carlId } }));
        var welcome = await SendAsync(ana, group, "g2", "Welcome Carl");
        Assert.Equal(HttpStatusCode.NotFound, (await carl.GetAsync($"/api/v1/conversations/{group}/messages/{old}/context")).StatusCode);
        var seen = await JsonAsync(await carl.GetAsync($"/api/v1/conversations/{group}/messages/{welcome}/context"));
        Assert.DoesNotContain(old, seen.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        Assert.Contains(welcome, seen.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        Assert.False(seen.GetProperty("hasOlder").GetBoolean());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Administrators_rebuild_the_index_and_retention_removes_entries(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db, new(FastIndexing) { ["Retention:MessageDays"] = "30", ["Retention:ClosedSessionDays"] = "0" });
        var (admin, adminId) = await SignedInAsync(api, "admin@example.org", "Admin");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = admin;
        using var _2 = ben;
        await using (var scope = api.Services.CreateAsyncScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<IUserAccountManager>().AddToRoleAsync(adminId, Roles.InstallationAdministrator, Ct)).Succeeded);
        }

        await BecomeContactsAsync(admin, adminId, ben, benId);
        var direct = await DirectAsync(admin, benId);
        await SendAsync(admin, direct, "m1", "Kickoff agenda");
        await WaitForAsync(ben, "kickoff", 1);

        var status = await JsonAsync(await admin.GetAsync("/api/v1/admin/search"));
        Assert.Equal("Database", status.GetProperty("provider").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.PostAsync("/api/v1/admin/search/rebuild")).StatusCode);
        var rebuilt = await JsonAsync(await admin.PostAsync("/api/v1/admin/search/rebuild"));
        Assert.Equal(1, rebuilt.GetProperty("queued").GetInt32());
        await WaitForAsync(ben, "kickoff", 1);

        // Search can be switched off live.
        AssertStatus(HttpStatusCode.OK, await admin.SendAsync(HttpMethod.Put, "/api/v1/admin/settings", new { values = new Dictionary<string, object?> { ["Search:Enabled"] = false } }));
        await api.Services.GetRequiredService<Audorix.ChatX.Api.Administration.RuntimeSettingsLoader>().ReloadAsync(Ct);
        Assert.Equal("search_disabled", await ErrorCodeAsync(await ben.GetAsync("/api/v1/search?q=kickoff")));
        AssertStatus(HttpStatusCode.OK, await admin.SendAsync(HttpMethod.Put, "/api/v1/admin/settings", new { values = new Dictionary<string, object?> { ["Search:Enabled"] = null } }));
        await api.Services.GetRequiredService<Audorix.ChatX.Api.Administration.RuntimeSettingsLoader>().ReloadAsync(Ct);

        // Message retention removes the index entry too.
        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<Application.Administration.DataRetentionService>().ApplyAsync(DateTimeOffset.UtcNow.AddDays(40), Ct);
        }

        await WaitForCountAsync(ben, "kickoff", 0);
    }

    private sealed record Hit(Guid Conversation, Guid Message);

    private static async Task<Guid> DirectAsync(ApiSession session, Guid otherId) =>
        (await JsonAsync(await session.PostAsync("/api/v1/conversations/direct", new { userId = otherId }))).GetProperty("id").GetGuid();

    private static async Task<Guid> SendAsync(ApiSession session, Guid conversation, string clientId, string body, Guid? attachment = null)
    {
        var response = await session.PostAsync($"/api/v1/conversations/{conversation}/messages",
            attachment is { } file ? new { clientMessageId = clientId, body, attachmentIds = new[] { file } } : new { clientMessageId = clientId, body });
        AssertStatus(HttpStatusCode.Created, response, await response.Content.ReadAsStringAsync(Ct));
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static IEnumerable<Guid> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("message").GetProperty("id").GetGuid());

    private static async Task<IReadOnlyList<Hit>> SearchAsync(ApiSession session, string query, string filters = "")
    {
        var response = await session.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(query)}{filters}");
        AssertStatus(HttpStatusCode.OK, response, await response.Content.ReadAsStringAsync(Ct));
        return (await JsonAsync(response)).GetProperty("items").EnumerateArray()
            .Select(i => new Hit(i.GetProperty("conversation").GetProperty("id").GetGuid(), i.GetProperty("message").GetProperty("id").GetGuid()))
            .ToList();
    }

    /// <summary>Indexing is asynchronous (the queue is applied every 100 ms here): wait for the expected number of results.</summary>
    private static async Task<IReadOnlyList<Hit>> WaitForAsync(ApiSession session, string query, int count)
    {
        IReadOnlyList<Hit> hits = [];
        for (var i = 0; i < 100; i++)
        {
            hits = await SearchAsync(session, query);
            if (hits.Count == count)
            {
                return hits;
            }

            await Task.Delay(100, Ct);
        }

        Assert.Fail($"Expected {count} result(s) for '{query}', got {hits.Count}.");
        return hits;
    }

    private static async Task WaitForCountAsync(ApiSession session, string query, int count) => await WaitForAsync(session, query, count);
}
