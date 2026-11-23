using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Audorix.ChatX.Application.Messaging;
using Audorix.ChatX.Application.Realtime;
using Audorix.ChatX.Contracts.V1.Realtime;
using Audorix.ChatX.Domain.Conversations;
using Audorix.ChatX.Infrastructure.Persistence;
using Audorix.ChatX.IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;
using static Audorix.ChatX.IntegrationTests.Support.TestUsers;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>
/// Phase 6 acceptance: persistence before publication (transactional outbox), idempotent sends,
/// edits/deletes/replies under the owner's rules, ADR-023 receipts, and realtime delivery only to
/// participants who may see the message — on real SQLite and PostgreSQL.
/// </summary>
public sealed class MessagingTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Sending_is_idempotent_by_client_message_id_even_when_retried_concurrently(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, ben, direct) = await DirectAsync(api);
        using var _1 = ana;
        using var _2 = ben;

        var first = await Send(ana, direct, "c-1", "Hello Ben");
        AssertStatus(HttpStatusCode.Created, first, await first.Content.ReadAsStringAsync(Ct));
        var message = await JsonAsync(first);
        Assert.Equal(("message", "Hello Ben", 1), (message.GetProperty("kind").GetString(), message.GetProperty("body").GetString(), message.GetProperty("version").GetInt32()));

        var retry = await Send(ana, direct, "c-1", "Hello Ben");
        AssertStatus(HttpStatusCode.OK, retry);
        Assert.Equal(message.GetProperty("id").GetGuid(), (await JsonAsync(retry)).GetProperty("id").GetGuid());
        Assert.Equal("idempotency_conflict", await ErrorCodeAsync(await Send(ana, direct, "c-1", "Something else")));

        // The same send racing with itself still yields one message.
        var racing = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Send(ana, direct, "c-2", "Are you there?")));
        Assert.All(racing, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict, r.StatusCode.ToString()));
        var bodies = (await HistoryAsync(ben, direct)).Where(e => e.GetProperty("kind").GetString() == "message").Select(e => e.GetProperty("body").GetString()).ToList();
        Assert.Equal(["Hello Ben", "Are you there?"], bodies);

        // Validation: empty, too long, missing id.
        Assert.Equal("message_body_required", await ErrorCodeAsync(await Send(ana, direct, "c-3", "  \n ")));
        Assert.Equal("message_body_too_long", await ErrorCodeAsync(await Send(ana, direct, "c-4", new string('x', 4001))));
        Assert.Equal("client_message_id_invalid", await ErrorCodeAsync(await Send(ana, direct, "", "Hi")));

        // Unread for Ben: the conversation start and two messages; his own reading clears it.
        var bensList = (await JsonAsync(await ben.GetAsync("/api/v1/conversations"))).EnumerateArray().Single();
        Assert.Equal(3, bensList.GetProperty("unreadCount").GetInt32());
        Assert.Equal(0, (await JsonAsync(await ana.GetAsync("/api/v1/conversations"))).EnumerateArray().Single().GetProperty("unreadCount").GetInt32());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Edits_deletes_and_replies_follow_the_rules(string provider)
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
        var group = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Team", userIds = new[] { benId, carlId } }))).GetProperty("id").GetGuid();

        var bens = await JsonAsync(await Send(ben, group, "b-1", "First draft"));
        var bensId = bens.GetProperty("id").GetGuid();

        // Only the author edits; edits bump the version.
        Assert.Equal("message_not_editable", await ErrorCodeAsync(await ana.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{group}/messages/{bensId}", new { body = "Hijacked" })));
        var edited = await JsonAsync(await ben.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{group}/messages/{bensId}", new { body = "Final text" }));
        Assert.Equal(("Final text", 2), (edited.GetProperty("body").GetString(), edited.GetProperty("version").GetInt32()));
        Assert.NotEqual(JsonValueKind.Null, edited.GetProperty("editedAtUtc").ValueKind);

        // Replies carry a preview; a message from another conversation cannot be quoted.
        var reply = await JsonAsync(await Send(carl, group, "c-1", "Agreed", bensId));
        Assert.Equal(("Final text", benId), (reply.GetProperty("replyTo").GetProperty("excerpt").GetString(), reply.GetProperty("replyTo").GetProperty("author").GetProperty("id").GetGuid()));
        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        Assert.Equal("message_not_found", await ErrorCodeAsync(await Send(ana, direct, "a-1", "Quoting another chat", bensId)));

        // Members delete only their own; owners and administrators also others'. Tombstones stay.
        var carls = reply.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{group}/messages/{carls}")).StatusCode);
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{group}/messages/{carls}"));
        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{group}/messages/{bensId}"));
        var history = (await HistoryAsync(ana, group)).Where(e => e.GetProperty("kind").GetString() == "message").ToList();
        Assert.All(history, m =>
        {
            Assert.Equal(JsonValueKind.Null, m.GetProperty("body").ValueKind);
            Assert.NotEqual(JsonValueKind.Null, m.GetProperty("deletedAtUtc").ValueKind);
        });
        Assert.True(history[1].GetProperty("replyTo").GetProperty("deleted").GetBoolean());
        Assert.Equal("message_not_found", await ErrorCodeAsync(await ben.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{group}/messages/{bensId}", new { body = "Back" })));

        // Deleted messages no longer count as unread.
        var annsView = (await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{group}"))).GetProperty("conversation");
        Assert.Equal(0, annsView.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task Messages_can_only_be_edited_within_the_edit_window()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, new() { ["Messaging:EditWindow"] = "00:00:00" });
        var (ana, _, direct) = await DirectAsync(api);
        using var _1 = ana;
        var id = (await JsonAsync(await Send(ana, direct, "x", "Typo"))).GetProperty("id").GetGuid();
        await Task.Delay(50, Ct);
        Assert.Equal("message_edit_window_expired", await ErrorCodeAsync(await ana.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{direct}/messages/{id}", new { body = "Fixed" })));
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{direct}/messages/{id}"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Receipts_are_forward_only_watermarks_and_reading_implies_delivery(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, ben, direct) = await DirectAsync(api);
        using var _1 = ana;
        using var _2 = ben;
        await Send(ana, direct, "1", "one");
        await Send(ana, direct, "2", "two");
        await Send(ana, direct, "3", "three");

        Assert.Equal(3, (await JsonAsync(await ben.PostAsync($"/api/v1/conversations/{direct}/delivered", new { sequence = 3 }))).GetProperty("lastDeliveredSequence").GetInt64());
        Assert.Equal(3, (await JsonAsync(await ben.PostAsync($"/api/v1/conversations/{direct}/delivered", new { sequence = 2 }))).GetProperty("lastDeliveredSequence").GetInt64());
        Assert.Equal(4, (await JsonAsync(await ben.PostAsync($"/api/v1/conversations/{direct}/delivered", new { sequence = 999 }))).GetProperty("lastDeliveredSequence").GetInt64());
        await ben.PostAsync($"/api/v1/conversations/{direct}/read", new { sequence = 2 });

        var bensReceipts = (await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{direct}"))).GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("user").GetProperty("displayName").GetString() == "Ben");
        Assert.Equal((4L, 2L), (bensReceipts.GetProperty("lastDeliveredSequence").GetInt64(), bensReceipts.GetProperty("lastReadSequence").GetInt64()));

        // Sending marks the sender's own message read and delivered.
        var anasReceipts = (await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{direct}"))).GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("user").GetProperty("displayName").GetString() == "Ana");
        Assert.Equal((4L, 4L), (anasReceipts.GetProperty("lastDeliveredSequence").GetInt64(), anasReceipts.GetProperty("lastReadSequence").GetInt64()));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Realtime_events_reach_only_participants_who_may_see_the_message(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        var (dana, _) = await SignedInAsync(api, "dana@example.org", "Dana");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        using var _4 = dana;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        await BecomeContactsAsync(ana, anaId, carl, carlId);
        var group = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Team", userIds = new[] { benId } }))).GetProperty("id").GetGuid();

        await using var anaHub = await HubDevice.ConnectAsync(api, "ana@example.org");
        await using var benHub = await HubDevice.ConnectAsync(api, "ben@example.org");
        await using var carlHub = await HubDevice.ConnectAsync(api, "carl@example.org");
        await using var danaHub = await HubDevice.ConnectAsync(api, "dana@example.org");
        var ben_ = Capture(benHub);
        var carl_ = Capture(carlHub);
        var dana_ = Capture(danaHub);
        var ana_ = Capture(anaHub);

        var sent = await JsonAsync(await Send(ana, group, "m1", "Before Carl joined"));
        var created = await ben_.Next("MessageCreated");
        Assert.Equal(("Before Carl joined", sent.GetProperty("id").GetGuid()), (created.Entry.Body, created.Entry.Id));

        // Carl joins later: he never receives events about messages from before his join.
        await ana.PostAsync($"/api/v1/conversations/{group}/participants", new { userIds = new[] { carlId } });
        await ana.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{group}/messages/{sent.GetProperty("id").GetGuid()}", new { body = "Edited before Carl" });
        Assert.Equal(2, (await ben_.Next("MessageUpdated")).Entry.Version);
        await Send(ana, group, "m2", "Welcome Carl");
        Assert.Equal("Welcome Carl", (await carl_.Next("MessageCreated")).Entry.Body);
        Assert.False(carl_.Has("MessageUpdated"));
        Assert.False(dana_.Any());

        // Receipts reach the other participants.
        await ben.PostAsync($"/api/v1/conversations/{group}/delivered", new { sequence = 99 });
        Assert.True((await ana_.NextReceipt("MessageDelivered")).UserId == benId);
        await ben.PostAsync($"/api/v1/conversations/{group}/read", new { sequence = 99 });
        Assert.True((await ana_.NextReceipt("MessageRead")).UserId == benId);

        // After removal nothing more arrives on the open connection.
        await ana.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{group}/participants/{benId}");
        await Send(ana, group, "m3", "After Ben left");
        Assert.Equal("After Ben left", (await carl_.Next("MessageCreated")).Entry.Body);
        await Task.Delay(500, Ct);
        Assert.DoesNotContain(ben_.All("MessageCreated"), e => e.Entry.Body == "After Ben left");
    }

    [Fact]
    public async Task Messages_are_saved_before_publication_and_published_later_when_the_transport_fails()
    {
        var flaky = new FlakyPublisher { FailuresLeft = 2 };
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, new()
        {
            ["Messaging:OutboxPollInterval"] = "00:00:00.100",
            ["Messaging:OutboxRetryDelay"] = "00:00:00.100",
        }, services =>
        {
            services.RemoveAll<IRealtimePublisher>();
            services.AddSingleton(flaky);
            services.AddSingleton<IRealtimePublisher>(sp =>
            {
                flaky.Inner = ActivatorUtilities.CreateInstance<Api.Realtime.SignalRRealtimePublisher>(sp);
                return flaky;
            });
        });
        var (ana, ben, direct) = await DirectAsync(api);
        using var _1 = ana;
        using var _2 = ben;
        await using var benHub = await HubDevice.ConnectAsync(api, "ben@example.org");
        var ben_ = Capture(benHub);

        var send = await Send(ana, direct, "o-1", "Survives a failing transport");
        AssertStatus(HttpStatusCode.Created, send);

        // The message is committed even though publishing failed; history has it immediately.
        Assert.Contains(await HistoryAsync(ben, direct), e => e.GetProperty("body").ValueKind == JsonValueKind.String && e.GetProperty("body").GetString() == "Survives a failing transport");

        // The outbox dispatcher retries and the event arrives; the outbox is then empty.
        Assert.Equal("Survives a failing transport", (await ben_.Next("MessageCreated")).Entry.Body);
        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        for (var i = 0; i < 50 && await context.OutboxMessages.AnyAsync(Ct); i++)
        {
            await Task.Delay(100, Ct);
        }

        Assert.False(await context.OutboxMessages.AnyAsync(Ct));
        Assert.Equal(0, flaky.FailuresLeft);
    }

    [Fact]
    public async Task Direct_conversations_go_quiet_across_a_block()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (ana, ben, direct) = await DirectAsync(api);
        using var _1 = ana;
        using var _2 = ben;
        var anaId = (await JsonAsync(await ana.GetAsync("/api/v1/users/me"))).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.NoContent, await ben.PostAsync($"/api/v1/contacts/{anaId}/block"));

        Assert.Equal("messaging_blocked", await ErrorCodeAsync(await Send(ana, direct, "b1", "Hello?")));
        Assert.Equal("messaging_blocked", await ErrorCodeAsync(await Send(ben, direct, "b2", "Hello?")));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Database_keeps_replies_inside_their_conversation_and_client_ids_unique(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        ana.Dispose();
        var now = DateTimeOffset.UtcNow;
        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        var one = Conversation.CreateGroup(Guid.NewGuid(), ConversationScope.Personal, null, "One", anaId, now);
        var two = Conversation.CreateGroup(Guid.NewGuid(), ConversationScope.Personal, null, "Two", anaId, now);
        var target = ConversationEvent.Post(Guid.NewGuid(), one, anaId, "t", "Target", null, 4000, now);
        context.AddRange(one, two, target);
        await context.SaveChangesAsync(Ct);

        // Forge a reply in conversation two that points at a message of conversation one.
        var forged = ConversationEvent.Post(Guid.NewGuid(), two, anaId, "f", "Forged", null, 4000, now);
        context.Entry(forged).Property(e => e.ReplyToEntryId).CurrentValue = target.Id;
        context.Add(forged);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));

        context.ChangeTracker.Clear();
        var reloaded = await context.Conversations.SingleAsync(c => c.Id == one.Id, Ct);
        context.Add(ConversationEvent.Post(Guid.NewGuid(), reloaded, anaId, "t", "Same client id", null, 4000, now));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<(ApiSession Ana, ApiSession Ben, Guid Direct)> DirectAsync(ChatXApiFactory api)
    {
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        return (ana, ben, direct);
    }

    private static Task<HttpResponseMessage> Send(ApiSession session, Guid conversationId, string clientMessageId, string body, Guid? replyToId = null) =>
        session.PostAsync($"/api/v1/conversations/{conversationId}/messages", new { clientMessageId, body, replyToId });

    private static async Task<List<JsonElement>> HistoryAsync(ApiSession session, Guid conversationId) =>
        (await JsonAsync(await session.GetAsync($"/api/v1/conversations/{conversationId}/history?limit=100"))).GetProperty("items").EnumerateArray().ToList();

    private static EventLog Capture(HubDevice device)
    {
        var log = new EventLog();
        foreach (var name in new[] { "MessageCreated", "MessageUpdated", "MessageDeleted" })
        {
            device.Hub.On<MessageEvent>(name, e => log.Add(name, e));
        }

        foreach (var name in new[] { "MessageDelivered", "MessageRead" })
        {
            device.Hub.On<ReceiptEvent>(name, e => log.AddReceipt(name, e));
        }

        return log;
    }

    private sealed class EventLog
    {
        private readonly ConcurrentQueue<(string Name, MessageEvent Event)> _messages = new();
        private readonly ConcurrentQueue<(string Name, ReceiptEvent Event)> _receipts = new();

        public void Add(string name, MessageEvent e) => _messages.Enqueue((name, e));

        public void AddReceipt(string name, ReceiptEvent e) => _receipts.Enqueue((name, e));

        public bool Has(string name) => _messages.Any(m => m.Name == name);

        public bool Any() => !_messages.IsEmpty || !_receipts.IsEmpty;

        public IEnumerable<MessageEvent> All(string name) => _messages.Where(m => m.Name == name).Select(m => m.Event);

        private readonly HashSet<Guid> _returned = [];
        private readonly Dictionary<string, int> _receiptsReturned = new(StringComparer.Ordinal);

        /// <summary>The next event of that name not returned before (events are at least once; duplicates share an EventId).</summary>
        public async Task<MessageEvent> Next(string name)
        {
            for (var i = 0; i < 100; i++)
            {
                var match = _messages.FirstOrDefault(m => m.Name == name && !_returned.Contains(m.Event.EventId));
                if (match.Event is not null)
                {
                    _returned.Add(match.Event.EventId);
                    return match.Event;
                }

                await Task.Delay(100, Ct);
            }

            throw new TimeoutException($"No new {name} event arrived.");
        }

        public async Task<ReceiptEvent> NextReceipt(string name)
        {
            for (var i = 0; i < 100; i++)
            {
                var all = _receipts.Where(m => m.Name == name).ToList();
                var taken = _receiptsReturned.GetValueOrDefault(name);
                if (all.Count > taken)
                {
                    _receiptsReturned[name] = taken + 1;
                    return all[taken].Event;
                }

                await Task.Delay(100, Ct);
            }

            throw new TimeoutException($"No new {name} event arrived.");
        }
    }

    /// <summary>Fails message notifications a given number of times, then forwards to SignalR.</summary>
    private sealed class FlakyPublisher : IRealtimePublisher
    {
        private int _failuresLeft;

        public int FailuresLeft
        {
            get => Volatile.Read(ref _failuresLeft);
            set => Volatile.Write(ref _failuresLeft, value);
        }

        public Api.Realtime.SignalRRealtimePublisher Inner { get; set; } = null!;

        public Task PublishToUsersAsync(IReadOnlyCollection<Guid> userIds, IRealtimeEvent realtimeEvent, CancellationToken cancellationToken)
        {
            if (realtimeEvent is MessageChanged && Interlocked.Decrement(ref _failuresLeft) >= 0)
            {
                throw new InvalidOperationException("Transport unavailable (test).");
            }

            if (_failuresLeft < 0)
            {
                Interlocked.Exchange(ref _failuresLeft, 0);
            }

            return Inner.PublishToUsersAsync(userIds, realtimeEvent, cancellationToken);
        }

        public Task PublishToConversationAsync(Guid conversationId, IRealtimeEvent realtimeEvent, CancellationToken cancellationToken) =>
            Inner.PublishToConversationAsync(conversationId, realtimeEvent, cancellationToken);

        public Task PublishToConnectionsAsync(IReadOnlyCollection<string> connectionIds, IRealtimeEvent realtimeEvent, CancellationToken cancellationToken) =>
            Inner.PublishToConnectionsAsync(connectionIds, realtimeEvent, cancellationToken);
    }
}
