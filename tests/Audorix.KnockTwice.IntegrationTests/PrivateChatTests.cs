using System.Net;
using System.Threading.Channels;
using Audorix.KnockTwice.Contracts.V1.Realtime;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Audorix.KnockTwice.IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// Private chats (owner request for Phase 7): one-to-one, accepted by invitation, held only in
/// memory for the life of the initiator's sign-in session, wiped with their files when they end,
/// and never written to the database or attachment storage.
/// </summary>
public sealed class PrivateChatTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_private_chat_lives_with_the_initiators_session_and_leaves_nothing_behind()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (anaSetup, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (benSetup, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carlSetup, _) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = anaSetup;
        using var _2 = benSetup;
        using var _3 = carlSetup;
        await BecomeContactsAsync(anaSetup, anaId, benSetup, benId);

        await using var ana = await HubDevice.ConnectAsync(api, "ana@example.org");
        await using var anaOtherDevice = await HubDevice.ConnectAsync(api, "ana@example.org");
        await using var ben = await HubDevice.ConnectAsync(api, "ben@example.org");
        var anaEvents = Events.Capture(ana);
        var benEvents = Events.Capture(ben);
        var anaOtherEvents = Events.Capture(anaOtherDevice);

        // Ana invites Ben; Ben is told and must accept before anything is said.
        var start = await ana.Session.PostAsync("/api/v1/private-chats", new { userId = benId });
        AssertStatus(HttpStatusCode.Created, start, await start.Content.ReadAsStringAsync(Ct));
        var chat = await JsonAsync(start);
        var chatId = chat.GetProperty("id").GetGuid();
        Assert.Equal(("invited", true), (chat.GetProperty("status").GetString(), chat.GetProperty("isInitiator").GetBoolean()));
        Assert.Equal(("invited", chatId), await benEvents.NextChangeAsync());

        var invitations = await JsonAsync(await ben.Session.GetAsync("/api/v1/private-chats"));
        Assert.Equal(chatId, invitations.EnumerateArray().Single().GetProperty("id").GetGuid());
        Assert.Equal("private_chat_not_active", await ErrorCodeAsync(await ben.Session.PostAsync($"/api/v1/private-chats/{chatId}/messages", new { clientMessageId = "b0", body = "Early" })));

        // The chat belongs to Ana's session: her other device and outsiders do not see it.
        Assert.Empty((await JsonAsync(await anaOtherDevice.Session.GetAsync("/api/v1/private-chats"))).EnumerateArray());
        Assert.Equal("private_chat_not_found", await ErrorCodeAsync(await anaOtherDevice.Session.GetAsync($"/api/v1/private-chats/{chatId}")));
        Assert.Equal("private_chat_not_found", await ErrorCodeAsync(await carlSetup.GetAsync($"/api/v1/private-chats/{chatId}")));

        AssertStatus(HttpStatusCode.OK, await ben.Session.PostAsync($"/api/v1/private-chats/{chatId}/accept"));
        Assert.Equal(("active", chatId), await anaEvents.NextChangeAsync("active"));

        // Messages and a file travel live and stay readable while the chat lasts.
        var photo = TestFiles.Png(2048);
        var upload = await ana.Session.UploadAsync($"/api/v1/private-chats/{chatId}/attachments", "secret.png", photo);
        AssertStatus(HttpStatusCode.Created, upload, await upload.Content.ReadAsStringAsync(Ct));
        var fileId = (await JsonAsync(upload)).GetProperty("id").GetGuid();
        Assert.Equal("attachment_not_found", await ErrorCodeAsync(await ben.Session.GetAsync($"/api/v1/private-chats/{chatId}/attachments/{fileId}/content")));

        var send = await ana.Session.PostAsync($"/api/v1/private-chats/{chatId}/messages", new { clientMessageId = "a1", body = "Only between us", attachmentIds = new[] { fileId } });
        AssertStatus(HttpStatusCode.Created, send, await send.Content.ReadAsStringAsync(Ct));
        AssertStatus(HttpStatusCode.OK, await ana.Session.PostAsync($"/api/v1/private-chats/{chatId}/messages", new { clientMessageId = "a1", body = "Only between us", attachmentIds = new[] { fileId } }));
        var live = await benEvents.NextMessageAsync();
        Assert.Equal(("Only between us", fileId), (live.Message.Body, live.Message.Attachments.Single().Id));

        var file = await ben.Session.GetAsync($"/api/v1/private-chats/{chatId}/attachments/{fileId}/content");
        AssertStatus(HttpStatusCode.OK, file);
        Assert.Equal(photo, await file.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("no-store", file.Headers.CacheControl!.ToString());

        AssertStatus(HttpStatusCode.Created, await ben.Session.PostAsync($"/api/v1/private-chats/{chatId}/messages", new { clientMessageId = "b1", body = "Agreed" }));
        var detail = await JsonAsync(await ben.Session.GetAsync($"/api/v1/private-chats/{chatId}"));
        Assert.Equal(["Only between us", "Agreed"], detail.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("body").GetString()));

        // Only the initiator ends an active chat.
        AssertStatus(HttpStatusCode.Forbidden, await ben.Session.SendAsync(HttpMethod.Delete, $"/api/v1/private-chats/{chatId}"));

        // Ana signs out: the chat ends for both, with everything in it.
        AssertStatus(HttpStatusCode.NoContent, await ana.Session.PostAsync("/api/v1/auth/logout"));
        Assert.Equal(("ended", chatId, "session_ended"), await benEvents.NextEndAsync());
        Assert.Equal("private_chat_not_found", await ErrorCodeAsync(await ben.Session.GetAsync($"/api/v1/private-chats/{chatId}")));
        Assert.Equal("private_chat_not_found", await ErrorCodeAsync(await ben.Session.GetAsync($"/api/v1/private-chats/{chatId}/attachments/{fileId}/content")));
        Assert.Empty((await JsonAsync(await ben.Session.GetAsync("/api/v1/private-chats"))).EnumerateArray());
        await anaOtherEvents.AssertNoneAsync();

        // Nothing was persisted: no conversation, history entry, attachment or stored file.
        await using var scope = api.Services.CreateAsyncScope();
        var data = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        Assert.Equal((0, 0, 0), (await data.Conversations.CountAsync(Ct), await data.ConversationEvents.CountAsync(Ct), await data.Attachments.CountAsync(Ct)));
        Assert.False(Directory.Exists(api.StorageRoot));
    }

    [Fact]
    public async Task Only_eligible_people_can_be_invited_and_invitations_can_be_declined_or_cancelled()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (anaSetup, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (benSetup, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carlSetup, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        var (danSetup, danId) = await SignedInAsync(api, "dan@example.org", "Dan");
        using var _1 = anaSetup;
        using var _2 = benSetup;
        using var _3 = carlSetup;
        using var _4 = danSetup;
        await BecomeContactsAsync(anaSetup, anaId, benSetup, benId);

        // Dan shares an organization with Ana; Carl has no relationship with her.
        await CreateOrganizationAsync(anaSetup, (danSetup, "dan@example.org"));

        await using var ana = await HubDevice.ConnectAsync(api, "ana@example.org");
        await using var ben = await HubDevice.ConnectAsync(api, "ben@example.org");
        var anaEvents = Events.Capture(ana);

        Assert.Equal("user_not_found", await ErrorCodeAsync(await ana.Session.PostAsync("/api/v1/private-chats", new { userId = carlId })));
        Assert.Equal("cannot_target_self", (await JsonAsync(await ana.Session.PostAsync("/api/v1/private-chats", new { userId = anaId })))
            .GetProperty("errors").GetProperty("userId")[0].GetString());

        // An organization colleague can be invited; Ana cancels before Dan answers.
        var withDan = (await JsonAsync(await ana.Session.PostAsync("/api/v1/private-chats", new { userId = danId }))).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.NoContent, await ana.Session.SendAsync(HttpMethod.Delete, $"/api/v1/private-chats/{withDan}"));
        Assert.Equal(("ended", withDan, "ended"), await anaEvents.NextEndAsync());

        // Ben declines; asking again while one runs returns the same invitation to Ana.
        var withBen = await JsonAsync(await ana.Session.PostAsync("/api/v1/private-chats", new { userId = benId }));
        var again = await ana.Session.PostAsync("/api/v1/private-chats", new { userId = benId });
        AssertStatus(HttpStatusCode.OK, again);
        Assert.Equal(withBen.GetProperty("id").GetGuid(), (await JsonAsync(again)).GetProperty("id").GetGuid());
        Assert.Equal("private_chat_exists", await ErrorCodeAsync(await ben.Session.PostAsync("/api/v1/private-chats", new { userId = anaId })));
        AssertStatus(HttpStatusCode.NoContent, await ben.Session.SendAsync(HttpMethod.Delete, $"/api/v1/private-chats/{withBen.GetProperty("id").GetGuid()}"));
        Assert.Equal(("ended", withBen.GetProperty("id").GetGuid(), "declined"), await anaEvents.NextEndAsync());

        // A block ends a running chat at once and prevents new ones.
        var blocked = (await JsonAsync(await ana.Session.PostAsync("/api/v1/private-chats", new { userId = benId }))).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.OK, await ben.Session.PostAsync($"/api/v1/private-chats/{blocked}/accept"));
        Assert.Equal("active", (await anaEvents.NextChangeAsync("active")).Status);
        AssertStatus(HttpStatusCode.NoContent, await ben.Session.PostAsync($"/api/v1/contacts/{anaId}/block"));
        Assert.Equal(("ended", blocked, "unavailable"), await anaEvents.NextEndAsync());
        Assert.Equal("user_not_found", await ErrorCodeAsync(await ana.Session.PostAsync("/api/v1/private-chats", new { userId = benId })));
    }

    [Fact]
    public async Task Revoking_the_initiators_session_from_another_device_ends_the_chat()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (anaSetup, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (benSetup, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = anaSetup;
        using var _2 = benSetup;
        await BecomeContactsAsync(anaSetup, anaId, benSetup, benId);
        await using var ana = await HubDevice.ConnectAsync(api, "ana@example.org");
        await using var ben = await HubDevice.ConnectAsync(api, "ben@example.org");
        var benEvents = Events.Capture(ben);

        var chatId = (await JsonAsync(await ana.Session.PostAsync("/api/v1/private-chats", new { userId = benId }))).GetProperty("id").GetGuid();
        Assert.Equal("invited", (await benEvents.NextChangeAsync()).Status);

        var current = (await JsonAsync(await ana.Session.GetAsync("/api/v1/auth/sessions"))).EnumerateArray().Single(s => s.GetProperty("current").GetBoolean());
        AssertStatus(HttpStatusCode.NoContent, await anaSetup.SendAsync(HttpMethod.Delete, $"/api/v1/auth/sessions/{current.GetProperty("id").GetGuid()}"));
        Assert.Equal(("ended", chatId, "session_ended"), await benEvents.NextEndAsync());
    }

    /// <summary>Records private chat events of one device.</summary>
    private sealed class Events
    {
        private readonly Channel<PrivateChatChangedEvent> _changes = Channel.CreateUnbounded<PrivateChatChangedEvent>();
        private readonly Channel<PrivateMessageEvent> _messages = Channel.CreateUnbounded<PrivateMessageEvent>();

        public static Events Capture(HubDevice device)
        {
            var events = new Events();
            device.Hub.On<PrivateChatChangedEvent>(RealtimeContract.Events.PrivateChatChanged, e => events._changes.Writer.TryWrite(e));
            device.Hub.On<PrivateMessageEvent>(RealtimeContract.Events.PrivateMessageCreated, e => events._messages.Writer.TryWrite(e));
            return events;
        }

        /// <summary>The next change; with <paramref name="status"/>, the next change to that status (both people get every change).</summary>
        public async Task<(string Status, Guid ChatId)> NextChangeAsync(string? status = null)
        {
            while (true)
            {
                var e = await Read(_changes);
                if (status is null || e.Status == status)
                {
                    return (e.Status, e.ChatId);
                }
            }
        }

        public async Task<(string Status, Guid ChatId, string? Reason)> NextEndAsync()
        {
            while (true)
            {
                var e = await Read(_changes);
                if (e.Status == "ended")
                {
                    return (e.Status, e.ChatId, e.Reason);
                }
            }
        }

        public Task<PrivateMessageEvent> NextMessageAsync() => Read(_messages);

        public async Task AssertNoneAsync()
        {
            await Task.Delay(500, Ct);
            Assert.False(_changes.Reader.TryRead(out var change), $"Unexpected event {change}");
            Assert.False(_messages.Reader.TryRead(out var message), $"Unexpected event {message}");
        }

        private static async Task<T> Read<T>(Channel<T> channel)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            return await channel.Reader.ReadAsync(timeout.Token);
        }
    }
}
