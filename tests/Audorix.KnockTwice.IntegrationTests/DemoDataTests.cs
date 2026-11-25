using System.Net;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Domain.Authorization;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Audorix.KnockTwice.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// Demo data (owner requests of 2026-11-23 and 2026-11-24): 300 people with the password Demo123!,
/// 50 companies, 150 groups, 800 channels, messages and activity (500 items for the administrator,
/// 50–100 for everyone else); loading is idempotent; clearing removes only demo data.
/// </summary>
public sealed class DemoDataTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Loading_is_idempotent_and_clearing_removes_only_demo_data(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        // Profile pictures (2026-11-24): avatar-NNN goes to demo person N. Two now, a third after the first load.
        var avatars = Directory.CreateTempSubdirectory("chatx-avatars-");
        await File.WriteAllBytesAsync(Path.Combine(avatars.FullName, "avatar-001.jpg"), Jpeg, Ct);
        await File.WriteAllBytesAsync(Path.Combine(avatars.FullName, "avatar-002.jpg"), Jpeg, Ct);
        await using var api = await StartAsync(db, new() { ["DemoData:Enabled"] = "true", ["DemoData:AvatarsPath"] = avatars.FullName });
        var (admin, adminId) = await AdministratorAsync(api);
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = admin;
        using var _2 = ben;

        // Real data first: a contact, a direct conversation and a group.
        await BecomeContactsAsync(admin, adminId, ben, benId);
        var direct = (await JsonAsync(await admin.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.Created, await admin.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "r1", body = "A real message" }));
        var group = (await JsonAsync(await admin.PostAsync("/api/v1/conversations/groups", new { title = "Real group", userIds = new[] { benId } }))).GetProperty("id").GetGuid();
        var real = await CountRowsAsync(api);

        var first = await admin.PostAsync("/api/v1/admin/demo-data");
        AssertStatus(HttpStatusCode.OK, first, await first.Content.ReadAsStringAsync(Ct));
        var loaded = await JsonAsync(first);
        var created = loaded.GetProperty("created");
        var plan = Application.Demo.DemoDataPlan.Create();
        var planned = plan.Messages.Where(m => m.Mentions is not null).Sum(m => m.Mentions!.Count) + plan.Saved.Count + plan.Reminders.Count + plan.Scheduled.Count;
        var supportMessages = plan.Support.Sum(s => s.Messages.Count);
        Assert.Equal((300, 50, plan.Messages.Count + supportMessages),
            (created.GetProperty("users").GetInt32(), created.GetProperty("organizations").GetInt32(), created.GetProperty("messages").GetInt32()));
        Assert.Equal((500, 500), (created.GetProperty("support").GetInt32(), loaded.GetProperty("total").GetProperty("support").GetInt32()));
        Assert.Equal(planned, created.GetProperty("activity").GetInt32());
        Assert.Equal(planned, loaded.GetProperty("total").GetProperty("activity").GetInt32());
        Assert.True(created.GetProperty("conversations").GetInt32() >= 1750, "150 groups, about 300 direct conversations, 800 channels and 500 support conversations");
        Assert.Equal(800, await CountChannelsAsync(api));
        Assert.True(created.GetProperty("contacts").GetInt32() >= 900);
        Assert.Equal("Demo123!", loaded.GetProperty("password").GetString());

        // The first two demo people have their pictures; the third keeps initials for now.
        Assert.Equal((2, 2), (created.GetProperty("photos").GetInt32(), loaded.GetProperty("total").GetProperty("photos").GetInt32()));
        var photo = await admin.GetAsync($"/api/v1/users/{plan.People[0].Id}/photo?v=1");
        AssertStatus(HttpStatusCode.OK, photo);
        Assert.Equal(Jpeg, await photo.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/users/{plan.People[2].Id}/photo?v=1")).StatusCode);
        await File.WriteAllBytesAsync(Path.Combine(avatars.FullName, "avatar-003.jpg"), Jpeg, Ct);
        // A file that is not an image is skipped; the load still succeeds.
        await File.WriteAllTextAsync(Path.Combine(avatars.FullName, "avatar-004.jpg"), "not an image", Ct);

        // The administrator who loads it is part of it, with 500 activity items (some mentions unread).
        Assert.True((await JsonAsync(await admin.GetAsync("/api/v1/organizations"))).GetArrayLength() >= 10);
        var mine = (await JsonAsync(await admin.GetAsync("/api/v1/conversations"))).EnumerateArray().ToList();
        Assert.True(mine.Count > 40, $"conversations: {mine.Count}");
        var activity = await ActivityAsync(api);
        Assert.Equal((400, 60, 25, 15), activity[adminId]);
        Assert.True((await JsonAsync(await admin.GetAsync("/api/v1/mentions/unread"))).GetProperty("unread").GetInt32() > 0);

        // Support (2026-11-24): the administrator is a support agent where the 500 conversations are,
        // with every status, assignments, notes and unread customer messages.
        var northwind = Application.Demo.DemoDataPlan.DemoId("org:northwind");
        Assert.True((await JsonAsync(await admin.GetAsync($"/api/v1/organizations/{northwind}"))).GetProperty("supportAgent").GetBoolean());
        var inbox = await JsonAsync(await admin.GetAsync($"/api/v1/organizations/{northwind}/support/inbox?status=all&take=100"));
        var counts = inbox.GetProperty("counts");
        Assert.Equal(120, counts.GetProperty("all").GetInt32());
        Assert.All(new[] { "open", "pending", "closed" }, s => Assert.True(counts.GetProperty(s).GetInt32() > 0, s));
        var items = inbox.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("conversation").GetProperty("assignedTo").ValueKind == System.Text.Json.JsonValueKind.Null);
        Assert.Contains(items, i => i.GetProperty("conversation").GetProperty("assignedTo").ValueKind != System.Text.Json.JsonValueKind.Null);
        Assert.All(items, i => Assert.False(string.IsNullOrEmpty(i.GetProperty("title").GetString())));
        Assert.Contains(items, i => i.GetProperty("conversation").GetProperty("unreadCount").GetInt32() > 0);
        Assert.Equal(plan.Support.Sum(s => s.Notes.Count), await CountSupportNotesAsync(api));

        // Everyone else: at least ten channels and 50 to 100 activity items.
        var channels = await ChannelsPerPersonAsync(api);
        Assert.True(channels[adminId] >= 10, $"administrator channels: {channels[adminId]}");
        Assert.Equal(300, activity.Count - 1);
        Assert.All(activity.Where(a => a.Key != adminId), a =>
        {
            Assert.True(channels.GetValueOrDefault(a.Key) >= 10, $"channels: {channels.GetValueOrDefault(a.Key)}");
            Assert.InRange(a.Value.Mentions + a.Value.Saved + a.Value.Reminders + a.Value.Scheduled, 50, 100);
        });

        // Every demo account signs in with Demo123!.
        foreach (var email in loaded.GetProperty("userEmails").EnumerateArray().Select(e => e.GetString()!).Take(3))
        {
            using var person = new ApiSession(api.CreateHttpsClient());
            AssertStatus(HttpStatusCode.OK, await person.LoginAsync(email, "Demo123!"));
        }

        // Leaving a demo group, then loading again: nothing duplicated, the departure respected.
        var hikers = mine.Single(c => c.GetProperty("title").GetString() == "Weekend hikers").GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.NoContent, await admin.PostAsync($"/api/v1/conversations/{hikers}/leave"));
        var afterLeave = await CountRowsAsync(api);
        var again = (await JsonAsync(await admin.PostAsync("/api/v1/admin/demo-data"))).GetProperty("created");
        Assert.Equal((0, 0, 0, 0, 0, 0, 0), (again.GetProperty("users").GetInt32(), again.GetProperty("organizations").GetInt32(),
            again.GetProperty("contacts").GetInt32(), again.GetProperty("conversations").GetInt32(), again.GetProperty("messages").GetInt32(),
            again.GetProperty("activity").GetInt32(), again.GetProperty("support").GetInt32()));
        Assert.Equal(afterLeave, await CountRowsAsync(api));
        // Loaded data gets the new picture; the ones already set are not set again.
        Assert.Equal(1, again.GetProperty("photos").GetInt32());
        Assert.Equal(3, (await JsonAsync(await admin.GetAsync("/api/v1/admin/demo-data"))).GetProperty("photos").GetInt32());
        var photoKeys = await PhotoKeysAsync(api);
        Assert.Equal(3, photoKeys.Count);

        // Clearing removes the demo data and nothing else.
        var cleared = await JsonAsync(await admin.SendAsync(HttpMethod.Delete, "/api/v1/admin/demo-data"));
        Assert.Equal(300, cleared.GetProperty("removed").GetProperty("users").GetInt32());
        Assert.Equal(500, cleared.GetProperty("removed").GetProperty("support").GetInt32());
        Assert.Equal(real, await CountRowsAsync(api));
        Assert.Equal(0, await CountSupportNotesAsync(api));
        Assert.Empty((await JsonAsync(await admin.GetAsync("/api/v1/organizations"))).EnumerateArray());
        Assert.Equal([direct, group], (await JsonAsync(await admin.GetAsync("/api/v1/conversations"))).EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid()).Order());
        var history = await JsonAsync(await ben.GetAsync($"/api/v1/conversations/{direct}/history"));
        Assert.Contains(history.GetProperty("items").EnumerateArray(), i => i.GetProperty("body").ValueKind == System.Text.Json.JsonValueKind.String);
        using (var alice = new ApiSession(api.CreateHttpsClient()))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await alice.LoginAsync("alice@demo.chatx.test", "Demo123!")).StatusCode);
        }

        var empty = await JsonAsync(await admin.GetAsync("/api/v1/admin/demo-data"));
        Assert.Equal((0, 0, 0), (empty.GetProperty("users").GetInt32(), empty.GetProperty("messages").GetInt32(), empty.GetProperty("photos").GetInt32()));
        Assert.Equal(0, (await JsonAsync(await admin.SendAsync(HttpMethod.Delete, "/api/v1/admin/demo-data"))).GetProperty("removed").GetProperty("users").GetInt32());

        // The demo photos' files went with the demo people.
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<Application.Attachments.IAttachmentStorage>();
            foreach (var key in photoKeys)
            {
                Assert.Null(await storage.OpenReadAsync(key, Ct));
            }
        }

        avatars.Delete(recursive: true);
    }

    /// <summary>The smallest data the server takes as a JPEG (it checks the signature only).</summary>
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9];

    private static async Task<List<string>> PhotoKeysAsync(ChatXApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ChatXDbContext>().UserProfiles
            .Where(p => p.PhotoKey != null).Select(p => p.PhotoKey!).ToListAsync(Ct);
    }

    [Fact]
    public async Task Only_administrators_may_load_or_clear_and_loading_only_where_enabled()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using (var enabled = await StartAsync(db, new() { ["DemoData:Enabled"] = "true" }))
        {
            var (user, _) = await SignedInAsync(enabled, "user@example.org", "User");
            using var _1 = user;
            Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/v1/admin/demo-data")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.SendAsync(HttpMethod.Delete, "/api/v1/admin/demo-data")).StatusCode);
            Assert.True((await JsonAsync(await user.GetAsync("/api/v1/auth/config"))).GetProperty("demoDataEnabled").GetBoolean());
        }

        await using var disabled = new ChatXApiFactory(new Dictionary<string, string?>
        {
            ["Database:Provider"] = db.Provider,
            ["Database:ConnectionString"] = db.ConnectionString,
        });
        var (admin, _) = await AdministratorAsync(disabled, "admin2@example.org");
        using var _2 = admin;
        var refused = await admin.PostAsync("/api/v1/admin/demo-data");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("demo_data_disabled", await ErrorCodeAsync(refused));
        Assert.False((await JsonAsync(await admin.GetAsync("/api/v1/auth/config"))).GetProperty("demoDataEnabled").GetBoolean());
    }

    private static async Task<(ApiSession Session, Guid Id)> AdministratorAsync(ChatXApiFactory api, string email = "admin@example.org")
    {
        var (session, id) = await SignedInAsync(api, email, "Admin");
        await using (var scope = api.Services.CreateAsyncScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<IUserAccountManager>().AddToRoleAsync(id, Roles.InstallationAdministrator, Ct)).Succeeded);
        }

        // Roles are read at sign-in. Loading demo data can take minutes while other tests share the
        // database server, so this session waits as long as the web app does (5 minutes).
        session.Dispose();
        var client = api.CreateHttpsClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        var admin = new ApiSession(client);
        AssertStatus(HttpStatusCode.OK, await admin.LoginAsync(email, Password));
        return (admin, id);
    }

    private static async Task<int> CountChannelsAsync(ChatXApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ChatXDbContext>().Conversations
            .CountAsync(c => c.Type == Domain.Conversations.ConversationType.Channel && c.IsDemo, Ct);
    }

    private static async Task<int> CountSupportNotesAsync(ChatXApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ChatXDbContext>().SupportNotes.CountAsync(Ct);
    }

    /// <summary>Mentions, saved messages, reminders and scheduled messages per person.</summary>
    private static async Task<Dictionary<Guid, (int Mentions, int Saved, int Reminders, int Scheduled)>> ActivityAsync(ChatXApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        var mentions = await context.MessageMentions.GroupBy(m => m.UserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, Ct);
        var saved = await context.SavedMessages.GroupBy(m => m.UserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, Ct);
        var reminders = await context.Reminders.GroupBy(m => m.UserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, Ct);
        var scheduled = await context.ScheduledMessages.GroupBy(m => m.UserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, Ct);
        return mentions.Keys.Union(saved.Keys).Union(reminders.Keys).Union(scheduled.Keys).ToDictionary(id => id,
            id => (mentions.GetValueOrDefault(id), saved.GetValueOrDefault(id), reminders.GetValueOrDefault(id), scheduled.GetValueOrDefault(id)));
    }

    /// <summary>Active channel memberships per person.</summary>
    private static async Task<Dictionary<Guid, int>> ChannelsPerPersonAsync(ChatXApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        var channels = context.Conversations.Where(c => c.Type == Domain.Conversations.ConversationType.Channel).Select(c => c.Id);
        return await context.ConversationParticipants
            .Where(p => channels.Contains(p.ConversationId) && p.Status == Domain.Conversations.ParticipantStatus.Active)
            .GroupBy(p => p.UserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, Ct);
    }

    private static async Task<(int Users, int Organizations, int Members, int Workspaces, int Contacts, int Conversations, int Participants, int Events)> CountRowsAsync(ChatXApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        return (
            await context.Users.CountAsync(Ct),
            await context.Organizations.CountAsync(Ct),
            await context.OrganizationMembers.CountAsync(Ct),
            await context.Workspaces.CountAsync(Ct),
            await context.Contacts.CountAsync(Ct),
            await context.Conversations.CountAsync(Ct),
            await context.ConversationParticipants.CountAsync(Ct),
            await context.ConversationEvents.CountAsync(Ct));
    }
}
