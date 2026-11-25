using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Audorix.KnockTwice.Application.Widgets;
using Audorix.KnockTwice.Contracts.V1.Realtime;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Audorix.KnockTwice.IntegrationTests.Support;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// The embedded chat (Phase 10, owner decisions): widgets managed by organization owners and
/// administrators; visitors anonymous (if allowed) or identified by an HS256 token the host server
/// signs with the widget secret; one conversation per visitor answered by chosen members;
/// per-address session and per-session message limits; attachments for identified visitors only.
/// </summary>
public sealed class WidgetTests
{
    private const string Shop = "https://shop.example.org";

    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task An_anonymous_visitor_talks_to_the_chosen_responders_and_nothing_else(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db, new() { ["Realtime:SessionCheckInterval"] = "00:00:01" });
        var (ana, _) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, _) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        var organizationId = await CreateOrganizationAsync(ana, (ben, "ben@example.org"), (carl, "carl@example.org"));
        var (widgetId, _) = await CreateWidgetAsync(ana, organizationId, responders: [benId]);

        // Members cannot manage widgets; the signing secret is never listed.
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.GetAsync($"/api/v1/organizations/{organizationId}/widgets")).StatusCode);
        var listed = await ana.GetAsync($"/api/v1/organizations/{organizationId}/widgets");
        Assert.DoesNotContain("secret", await listed.Content.ReadAsStringAsync(Ct), StringComparison.OrdinalIgnoreCase);

        // Public configuration and the frame policy the web server enforces.
        using var anonymous = api.CreateHttpsClient();
        var config = await JsonAsync(await anonymous.GetAsync($"/api/v1/widget/{widgetId}/config", Ct));
        Assert.Equal(("Chat with us", true), (config.GetProperty("title").GetString(), config.GetProperty("allowAnonymous").GetBoolean()));
        var policy = await anonymous.GetAsync($"/api/v1/widget/{widgetId}/frame-policy", Ct);
        Assert.Equal(Shop, policy.Headers.GetValues("X-ChatX-Frame-Ancestors").Single());
        var unknown = await anonymous.GetAsync($"/api/v1/widget/{Guid.NewGuid()}/frame-policy", Ct);
        Assert.Equal("'none'", unknown.Headers.GetValues("X-ChatX-Frame-Ancestors").Single());

        // Sessions only start from an allowed page.
        var foreign = await anonymous.PostAsJsonAsync($"/api/v1/widget/{widgetId}/sessions", new { parentOrigin = "https://evil.example" }, Ct);
        Assert.Equal((HttpStatusCode.Forbidden, "widget_origin_not_allowed"), (foreign.StatusCode, await ErrorCodeAsync(foreign)));
        var token = await StartSessionAsync(anonymous, widgetId);
        using var visitor = VisitorClient(api, token);

        var empty = await JsonAsync(await visitor.GetAsync("/api/v1/widget/visitor/conversation", Ct));
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("conversationId").ValueKind);

        // A visitor token is not a sign-in: the regular API and the chat hub refuse it.
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/v1/conversations", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/v1/users/me", Ct)).StatusCode);

        await using var visitorHub = await WidgetHubClient.ConnectAsync(api, token);
        var first = await visitor.PostAsJsonAsync("/api/v1/widget/visitor/messages", new { clientMessageId = "v1", body = "Is this in stock?" }, Ct);
        AssertStatus(HttpStatusCode.Created, first, await first.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsJsonAsync("/api/v1/widget/visitor/messages", new { clientMessageId = "v1", body = "Is this in stock?" }, Ct)).StatusCode);

        // The responder sees a website conversation in the normal list and answers there.
        var benList = await JsonAsync(await ben.GetAsync("/api/v1/conversations"));
        var website = benList.EnumerateArray().Single();
        Assert.Equal(("website", "organization"), (website.GetProperty("type").GetString(), website.GetProperty("scope").GetString()));
        var conversationId = website.GetProperty("id").GetGuid();
        Assert.Empty((await JsonAsync(await carl.GetAsync("/api/v1/conversations"))).EnumerateArray());
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await carl.GetAsync($"/api/v1/conversations/{conversationId}/history")));

        AssertStatus(HttpStatusCode.Created, await ben.PostAsync($"/api/v1/conversations/{conversationId}/messages", new { clientMessageId = "b1", body = "Yes, two left." }));
        // Delivery is at least once (outbox retries), so wait for the reply rather than count events.
        Assert.Equal(conversationId, (await visitorHub.NextMessageAsync("Yes, two left.")).ConversationId);

        var thread = await JsonAsync(await visitor.GetAsync("/api/v1/widget/visitor/conversation", Ct));
        Assert.Equal(conversationId, thread.GetProperty("conversationId").GetGuid());
        Assert.Equal(["Is this in stock?", "Yes, two left."],
            thread.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("kind").GetString() == "message").Select(i => i.GetProperty("body").GetString()));
        Assert.Equal("Ben", thread.GetProperty("responders").EnumerateArray().Single().GetProperty("displayName").GetString());
        var read = await visitor.PostAsJsonAsync("/api/v1/widget/visitor/read", new { sequence = thread.GetProperty("items").EnumerateArray().Last().GetProperty("sequence").GetInt64() }, Ct);
        AssertStatus(HttpStatusCode.OK, read, await read.Content.ReadAsStringAsync(Ct));

        // Anonymous visitors cannot attach files.
        var upload = await UploadAsync(visitor, "/api/v1/widget/visitor/attachments", "photo.png", TestFiles.Png(1024));
        Assert.Equal((HttpStatusCode.Forbidden, "widget_attachments_identified_only"), (upload.StatusCode, await ErrorCodeAsync(upload)));

        // Typing reaches the responder's conversation group.
        await visitorHub.Hub.InvokeAsync("SendTypingStatus", true, Ct);

        // A second visitor has its own conversation and cannot see the first one.
        var otherToken = await StartSessionAsync(anonymous, widgetId);
        using var other = VisitorClient(api, otherToken);
        var otherThread = await JsonAsync(await other.GetAsync("/api/v1/widget/visitor/conversation", Ct));
        Assert.Equal(JsonValueKind.Null, otherThread.GetProperty("conversationId").ValueKind);

        // Visitor accounts never show up in people search.
        Assert.DoesNotContain("Visitor", await (await ben.GetAsync("/api/v1/users/search?q=visitor")).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Ending the session, or switching the widget off, ends the visitor's access.
        AssertStatus(HttpStatusCode.NoContent, await other.DeleteAsync("/api/v1/widget/visitor/session", Ct));
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/widget/visitor/conversation", Ct)).StatusCode);

        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{organizationId}/widgets/{widgetId}", new { enabled = false }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/v1/widget/visitor/conversation", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/widget/{widgetId}/config", Ct)).StatusCode);
        Assert.Equal("'none'", (await anonymous.GetAsync($"/api/v1/widget/{widgetId}/frame-policy", Ct)).Headers.GetValues("X-ChatX-Frame-Ancestors").Single());
        await visitorHub.Closed.WaitAsync(TimeSpan.FromSeconds(15), Ct);

        // The organization keeps the conversation.
        Assert.Equal(conversationId, (await JsonAsync(await ben.GetAsync("/api/v1/conversations"))).EnumerateArray().Single().GetProperty("id").GetGuid());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Identified_visitors_need_a_valid_token_from_the_host_server(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        using var _1 = ana;
        var organizationId = await CreateOrganizationAsync(ana);
        var (widgetId, secret) = await CreateWidgetAsync(ana, organizationId, responders: [anaId], allowAnonymous: false);
        using var browser = api.CreateHttpsClient();
        var now = DateTimeOffset.UtcNow;

        var anonymous = await browser.PostAsJsonAsync($"/api/v1/widget/{widgetId}/sessions", new { parentOrigin = Shop }, Ct);
        Assert.Equal((HttpStatusCode.Unauthorized, "widget_identity_required"), (anonymous.StatusCode, await ErrorCodeAsync(anonymous)));

        // Every malformed or untrusted token is refused the same way.
        string[] invalid =
        [
            IdentityTokens.Sign("not-the-secret", widgetId, "u-1", "Dora", null, now, TimeSpan.FromMinutes(5)),
            IdentityTokens.Sign(secret, Guid.NewGuid(), "u-1", "Dora", null, now, TimeSpan.FromMinutes(5)),
            IdentityTokens.Sign(secret, widgetId, "u-1", "Dora", null, now.AddHours(-2), TimeSpan.FromMinutes(5)),
            IdentityTokens.Sign(secret, widgetId, "u-1", "Dora", null, now, TimeSpan.FromDays(2)),
            Unsigned(widgetId, now),
            "not.a.token",
        ];
        foreach (var bad in invalid)
        {
            var refused = await browser.PostAsJsonAsync($"/api/v1/widget/{widgetId}/sessions", new { identityToken = bad, parentOrigin = Shop }, Ct);
            Assert.Equal((HttpStatusCode.Unauthorized, "widget_identity_invalid"), (refused.StatusCode, await ErrorCodeAsync(refused)));
        }

        var good = IdentityTokens.Sign(secret, widgetId, "u-1", "Dora", "dora@shop.example", now, TimeSpan.FromMinutes(5));
        var started = await JsonAsync(await browser.PostAsJsonAsync($"/api/v1/widget/{widgetId}/sessions", new { identityToken = good, parentOrigin = Shop }, Ct));
        Assert.Equal(("Dora", true), (started.GetProperty("displayName").GetString(), started.GetProperty("identified").GetBoolean()));
        using var dora = VisitorClient(api, started.GetProperty("token").GetString()!);

        // Identified visitors may attach files and read them back.
        var photo = TestFiles.Png(2048);
        var upload = await UploadAsync(dora, "/api/v1/widget/visitor/attachments", "receipt.png", photo);
        AssertStatus(HttpStatusCode.Created, upload, await upload.Content.ReadAsStringAsync(Ct));
        var fileId = (await JsonAsync(upload)).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.Created, await dora.PostAsJsonAsync("/api/v1/widget/visitor/messages", new { clientMessageId = "d1", body = "My receipt", attachmentIds = new[] { fileId } }, Ct));
        var content = await dora.GetAsync($"/api/v1/widget/visitor/attachments/{fileId}/content", Ct);
        AssertStatus(HttpStatusCode.OK, content);
        Assert.Equal(photo, await content.Content.ReadAsByteArrayAsync(Ct));

        // The same subject on another device continues the same conversation, under its new name.
        var again = IdentityTokens.Sign(secret, widgetId, "u-1", "Dora Diaz", null, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        var second = await JsonAsync(await browser.PostAsJsonAsync($"/api/v1/widget/{widgetId}/sessions", new { identityToken = again, parentOrigin = Shop }, Ct));
        Assert.Equal(started.GetProperty("visitorId").GetGuid(), second.GetProperty("visitorId").GetGuid());
        using var doraPhone = VisitorClient(api, second.GetProperty("token").GetString()!);
        var thread = await JsonAsync(await doraPhone.GetAsync("/api/v1/widget/visitor/conversation", Ct));
        Assert.Equal("My receipt", thread.GetProperty("items").EnumerateArray().Last().GetProperty("body").GetString());
        var summary = (await JsonAsync(await ana.GetAsync("/api/v1/conversations"))).EnumerateArray().Single();
        Assert.Equal("Dora Diaz", summary.GetProperty("title").GetString());

        // Replacing the secret invalidates tokens signed with the old one.
        var rotated = await JsonAsync(await ana.PostAsync($"/api/v1/organizations/{organizationId}/widgets/{widgetId}/secret"));
        Assert.NotEqual(secret, rotated.GetProperty("signingSecret").GetString());
        var stale = IdentityTokens.Sign(secret, widgetId, "u-1", "Dora", null, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync($"/api/v1/widget/{widgetId}/sessions", new { identityToken = stale, parentOrigin = Shop }, Ct)).StatusCode);

        // The secret is stored protected, never in plain text.
        await using var scope = api.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ChatXDbContext>().Widgets.SingleAsync(Ct);
        Assert.DoesNotContain(rotated.GetProperty("signingSecret").GetString()!, stored.ProtectedSecret, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Visitor_limits_apply_per_address_and_per_session()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, new()
        {
            ["Widgets:AnonymousSessionsPerHourPerAddress"] = "2",
            ["Widgets:MessagesPerMinute"] = "2",
        });
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        using var _1 = ana;
        var organizationId = await CreateOrganizationAsync(ana);
        var (widgetId, _) = await CreateWidgetAsync(ana, organizationId, responders: [anaId]);
        using var browser = api.CreateHttpsClient();

        var token = await StartSessionAsync(browser, widgetId);
        await StartSessionAsync(browser, widgetId);
        var third = await browser.PostAsJsonAsync($"/api/v1/widget/{widgetId}/sessions", new { parentOrigin = Shop }, Ct);
        Assert.Equal((HttpStatusCode.TooManyRequests, "too_many_requests"), (third.StatusCode, await ErrorCodeAsync(third)));

        using var visitor = VisitorClient(api, token);
        for (var i = 0; i < 2; i++)
        {
            AssertStatus(HttpStatusCode.Created, await visitor.PostAsJsonAsync("/api/v1/widget/visitor/messages", new { clientMessageId = $"m{i}", body = "Hello" }, Ct));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await visitor.PostAsJsonAsync("/api/v1/widget/visitor/messages", new { clientMessageId = "m9", body = "Hello" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Widgets_are_only_switched_on_when_they_can_be_answered()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (ana, _) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (zed, zedId) = await SignedInAsync(api, "zed@example.org", "Zed");
        using var _1 = ana;
        using var _2 = zed;
        var organizationId = await CreateOrganizationAsync(ana);
        var created = await ana.PostAsync($"/api/v1/organizations/{organizationId}/widgets", new { name = "Shop" });
        AssertStatus(HttpStatusCode.Created, created, await created.Content.ReadAsStringAsync(Ct));
        var body = await JsonAsync(created);
        var widgetId = body.GetProperty("widget").GetProperty("id").GetGuid();
        Assert.False(body.GetProperty("widget").GetProperty("enabled").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("signingSecret").GetString()));
        var path = $"/api/v1/organizations/{organizationId}/widgets/{widgetId}";

        Assert.Equal("widget_origins_required", await ErrorCodeAsync(await ana.SendAsync(HttpMethod.Put, path, new { enabled = true })));
        Assert.Equal("widget_origin_invalid", await ErrorCodeAsync(await ana.SendAsync(HttpMethod.Put, path, new { allowedOrigins = new[] { "https://*.example.org" } })));
        Assert.Equal("widget_origin_invalid", await ErrorCodeAsync(await ana.SendAsync(HttpMethod.Put, path, new { allowedOrigins = new[] { "https://shop.example.org/page" } })));
        Assert.Contains("responder_not_member", await (await ana.SendAsync(HttpMethod.Put, path, new { responderIds = new[] { zedId } })).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Contains("widget_responders_required",
            await (await ana.SendAsync(HttpMethod.Put, path, new { allowedOrigins = new[] { Shop }, responderIds = Array.Empty<Guid>(), enabled = true })).Content.ReadAsStringAsync(Ct),
            StringComparison.Ordinal);
        Assert.Equal("widget_color_invalid", await ErrorCodeAsync(await ana.SendAsync(HttpMethod.Put, path, new { accentColor = "red" })));

        var ok = await JsonAsync(await ana.SendAsync(HttpMethod.Put, path, new { allowedOrigins = new[] { "HTTPS://Shop.Example.org:443/" }, enabled = true, position = "left" }));
        Assert.Equal([Shop], ok.GetProperty("allowedOrigins").EnumerateArray().Select(o => o.GetString()));
        Assert.Equal(("left", true), (ok.GetProperty("position").GetString(), ok.GetProperty("enabled").GetBoolean()));

        // Outsiders cannot see the organization's widgets.
        Assert.Equal(HttpStatusCode.NotFound, (await zed.GetAsync(path)).StatusCode);
    }

    private static async Task<(Guid Id, string Secret)> CreateWidgetAsync(ApiSession owner, Guid organizationId, Guid[] responders, bool allowAnonymous = true)
    {
        var created = await JsonAsync(await owner.PostAsync($"/api/v1/organizations/{organizationId}/widgets", new { name = "Shop" }));
        var id = created.GetProperty("widget").GetProperty("id").GetGuid();
        var update = await owner.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{organizationId}/widgets/{id}", new
        {
            title = "Chat with us",
            allowedOrigins = new[] { Shop },
            responderIds = responders,
            allowAnonymous,
            enabled = true,
        });
        AssertStatus(HttpStatusCode.OK, update, await update.Content.ReadAsStringAsync(Ct));
        return (id, created.GetProperty("signingSecret").GetString()!);
    }

    private static async Task<string> StartSessionAsync(HttpClient client, Guid widgetId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/widget/{widgetId}/sessions", new { parentOrigin = Shop, language = "en" }, Ct);
        AssertStatus(HttpStatusCode.OK, response, await response.Content.ReadAsStringAsync(Ct));
        return (await JsonAsync(response)).GetProperty("token").GetString()!;
    }

    private static HttpClient VisitorClient(ChatXApiFactory api, string token)
    {
        var client = api.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string path, string fileName, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", fileName);
        return await client.PostAsync(path, form, Ct);
    }

    /// <summary>An "alg: none" token: must never be accepted.</summary>
    private static string Unsigned(Guid widgetId, DateTimeOffset now)
    {
        static string Encode(string json) => IdentityTokens.Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var payload = $$"""{"sub":"u-1","aud":"{{widgetId}}","iat":{{now.ToUnixTimeSeconds()}},"exp":{{now.AddMinutes(5).ToUnixTimeSeconds()}}}""";
        return $"{Encode("""{"alg":"none","typ":"JWT"}""")}.{Encode(payload)}.";
    }

    /// <summary>A visitor's realtime connection to <c>/hubs/widget</c>.</summary>
    private sealed class WidgetHubClient : IAsyncDisposable
    {
        private readonly Channel<MessageEvent> _messages = Channel.CreateUnbounded<MessageEvent>();
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private WidgetHubClient(HubConnection hub)
        {
            Hub = hub;
            hub.On<MessageEvent>("MessageCreated", e => _messages.Writer.TryWrite(e));
            hub.Closed += _ =>
            {
                _closed.TrySetResult();
                return Task.CompletedTask;
            };
        }

        public HubConnection Hub { get; }

        public Task Closed => _closed.Task;

        public static async Task<WidgetHubClient> ConnectAsync(ChatXApiFactory api, string token)
        {
            var hub = new HubConnectionBuilder()
                .WithUrl(new Uri("https://localhost/hubs/widget"), options =>
                {
                    options.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                })
                .Build();
            var client = new WidgetHubClient(hub);
            await hub.StartAsync(Ct);
            return client;
        }

        public async Task<MessageEvent> NextMessageAsync(string body)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (true)
            {
                var next = await _messages.Reader.ReadAsync(timeout.Token);
                if (next.Entry.Body == body)
                {
                    return next;
                }
            }
        }

        public async ValueTask DisposeAsync() => await Hub.DisposeAsync();
    }
}
