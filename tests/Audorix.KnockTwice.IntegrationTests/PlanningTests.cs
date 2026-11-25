using System.Net;
using System.Text.Json;
using Audorix.KnockTwice.IntegrationTests.Support;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// Drafts, scheduled messages and reminders (Phase 20, owner decisions of 2026-11-23): drafts sync
/// text and reply target per conversation and thread; scheduled messages go out with the normal
/// rules and, when that fails, stay listed with the reason; reminders notify in the app, by push
/// and by email.
/// </summary>
public sealed class PlanningTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Fast => new()
    {
        ["Planning:DispatchInterval"] = "00:00:00.200",
        ["Notifications:DispatchInterval"] = "00:00:00.200",
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Drafts_are_kept_per_conversation_and_thread(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db, Fast);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        var hello = (await JsonAsync(await ben.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "b1", body = "Hello" })))
            .GetProperty("id").GetGuid();
        var draft = $"/api/v1/conversations/{direct}/draft";

        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Put, draft, new { body = "Half a tho", replyToId = hello }));
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Put, draft, new { body = "Half a thought ", replyToId = hello }));
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Put, draft, new { body = "In the thread", threadRootId = hello }));
        var drafts = (await JsonAsync(await ana.GetAsync("/api/v1/drafts"))).EnumerateArray().ToList();
        Assert.Equal(2, drafts.Count);
        var main = drafts.Single(d => d.GetProperty("threadRootId").ValueKind == JsonValueKind.Null);
        Assert.Equal("Half a thought ", main.GetProperty("body").GetString());
        Assert.Equal(hello, main.GetProperty("replyToId").GetGuid());
        Assert.Equal(hello, drafts.Single(d => d.GetProperty("threadRootId").ValueKind != JsonValueKind.Null).GetProperty("threadRootId").GetGuid());

        // Private to the person; emptied drafts disappear; too long is refused.
        Assert.Empty((await JsonAsync(await ben.GetAsync("/api/v1/drafts"))).EnumerateArray());
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Put, draft, new { body = "  " }));
        Assert.Single((await JsonAsync(await ana.GetAsync("/api/v1/drafts"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.SendAsync(HttpMethod.Put, draft, new { body = new string('x', 4001) })).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Scheduled_messages_go_out_on_time_or_stay_listed_with_the_reason(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db, Fast);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        var group = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Team", userIds = new[] { benId } }))).GetProperty("id").GetGuid();

        // Times must be in the next 30 days.
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsync($"/api/v1/conversations/{direct}/scheduled",
            new { body = "Too late", sendAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsync($"/api/v1/conversations/{direct}/scheduled",
            new { body = "Too far", sendAtUtc = DateTimeOffset.UtcNow.AddDays(31) })).StatusCode);

        // A message with a file, a little later; one far ahead that is edited then cancelled.
        var upload = await JsonAsync(await ana.UploadAsync($"/api/v1/conversations/{direct}/attachments", "plan.png", TestFiles.Png(1024), "image/png"));
        var soon = await ana.PostAsync($"/api/v1/conversations/{direct}/scheduled",
            new { body = "Good morning!", sendAtUtc = DateTimeOffset.UtcNow.AddSeconds(20), attachmentIds = new[] { upload.GetProperty("id").GetGuid() } });
        AssertStatus(HttpStatusCode.Created, soon, await soon.Content.ReadAsStringAsync(Ct));
        Assert.Equal(1, (await JsonAsync(soon)).GetProperty("fileCount").GetInt32());
        var later = (await JsonAsync(await ana.PostAsync($"/api/v1/conversations/{direct}/scheduled",
            new { body = "Weekly report", sendAtUtc = DateTimeOffset.UtcNow.AddDays(2) }))).GetProperty("id").GetGuid();
        var edited = await JsonAsync(await ana.SendAsync(HttpMethod.Put, $"/api/v1/scheduled/{later}", new { body = "Weekly report (v2)", sendAtUtc = DateTimeOffset.UtcNow.AddDays(3) }));
        Assert.Equal("Weekly report (v2)", edited.GetProperty("body").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await ben.SendAsync(HttpMethod.Delete, $"/api/v1/scheduled/{later}")).StatusCode);
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/scheduled/{later}"));

        // One in the group, after which Ana leaves: it cannot be sent.
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{group}/scheduled", new { body = "Bye all", sendAtUtc = DateTimeOffset.UtcNow.AddSeconds(20) }));
        AssertStatus(HttpStatusCode.NoContent, await ana.PostAsync($"/api/v1/conversations/{group}/leave"));

        JsonElement? sent = null;
        JsonElement? failed = null;
        for (var i = 0; i < 900 && (sent is null || failed is null); i++)
        {
            await Task.Delay(100, Ct);
            var history = await JsonAsync(await ben.GetAsync($"/api/v1/conversations/{direct}/history"));
            sent = history.GetProperty("items").EnumerateArray().Where(e => e.GetProperty("kind").GetString() == "message").Select(e => (JsonElement?)e).FirstOrDefault();
            failed = (await JsonAsync(await ana.GetAsync("/api/v1/scheduled"))).EnumerateArray()
                .Where(s => s.GetProperty("status").GetString() == "failed").Select(s => (JsonElement?)s).FirstOrDefault();
        }

        Assert.NotNull(sent);
        Assert.Equal("Good morning!", sent.Value.GetProperty("body").GetString());
        Assert.Equal(anaId, sent.Value.GetProperty("actor").GetProperty("id").GetGuid());
        Assert.Equal(upload.GetProperty("id").GetGuid(), sent.Value.GetProperty("attachments")[0].GetProperty("id").GetGuid());
        Assert.NotNull(failed);
        Assert.Equal("conversation_not_found", failed.Value.GetProperty("failureCode").GetString());
        Assert.Equal("Bye all", failed.Value.GetProperty("body").GetString());

        // Sent ones leave the list; the failed one stays until removed.
        var list = (await JsonAsync(await ana.GetAsync("/api/v1/scheduled"))).EnumerateArray().ToList();
        Assert.Equal("failed", Assert.Single(list).GetProperty("status").GetString());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Reminders_come_due_and_are_emailed_when_away(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db, Fast);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        var message = (await JsonAsync(await ben.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "b1", body = "Call the **bank**" })))
            .GetProperty("id").GetGuid();
        var reminder = $"/api/v1/conversations/{direct}/messages/{message}/reminder";

        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsync(reminder, new { remindAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5) })).StatusCode);
        var created = await JsonAsync(await ana.PostAsync(reminder, new { remindAtUtc = DateTimeOffset.UtcNow.AddSeconds(20) }));
        Assert.Equal("pending", created.GetProperty("status").GetString());
        Assert.Equal(message, created.GetProperty("entry").GetProperty("id").GetGuid());
        Assert.Empty((await JsonAsync(await ben.GetAsync("/api/v1/reminders"))).EnumerateArray());

        Application.Abstractions.EmailMessage? email = null;
        for (var i = 0; i < 900 && email is null; i++)
        {
            await Task.Delay(100, Ct);
            email = api.Emails.To("ana@example.org", "reminder") is [var first, ..] ? first : null;
        }

        Assert.NotNull(email);
        Assert.Contains("Call the bank", email.TextBody, StringComparison.Ordinal);
        Assert.Contains($"message={message}", email.TextBody, StringComparison.Ordinal);
        var due = Assert.Single((await JsonAsync(await ana.GetAsync("/api/v1/reminders"))).EnumerateArray());
        Assert.Equal("due", due.GetProperty("status").GetString());

        // Done: gone; someone else's reminder cannot be touched.
        Assert.Equal(HttpStatusCode.NotFound, (await ben.SendAsync(HttpMethod.Delete, $"/api/v1/reminders/{due.GetProperty("id").GetGuid()}")).StatusCode);
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/reminders/{due.GetProperty("id").GetGuid()}"));
        Assert.Empty((await JsonAsync(await ana.GetAsync("/api/v1/reminders"))).EnumerateArray());
    }
}
