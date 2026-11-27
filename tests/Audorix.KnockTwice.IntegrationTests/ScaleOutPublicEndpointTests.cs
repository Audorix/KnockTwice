using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Audorix.KnockTwice.Contracts.V1.Realtime;
using Microsoft.AspNetCore.SignalR.Client;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// External two-node acceptance tests for docker/compose.scaleout.yaml. Unlike ScaleOutTests,
/// these use a real Redis backplane, HAProxy, PostgreSQL and network SignalR connections.
/// </summary>
public sealed class ScaleOutPublicEndpointTests
{
    [Fact]
    public async Task Affinity_keeps_each_client_on_a_distinct_node_and_backplane_delivers_channel_and_direct_messages()
    {
        var setup = GetSetup();
        if (setup is null) return;
        var expectBackplane = !string.Equals(Environment.GetEnvironmentVariable("CHATX_SCALEOUT_EXPECT_BACKPLANE"), "false", StringComparison.OrdinalIgnoreCase);

        await using var ana = await BrowserSession.SignInAsync(setup.BaseUri, setup.AnaEmail, setup.AnaPassword);
        await using var ben = await BrowserSession.SignInAsync(setup.BaseUri, setup.BenEmail, setup.BenPassword);
        Assert.NotEqual(ana.Node, ben.Node);
        Console.WriteLine($"Affinity assignments: Ana={ana.Node}; Ben={ben.Node}; expected backplane={expectBackplane}.");

        await using var anaHub = await HubDevice.ConnectAsync(setup.BaseUri, ana.Cookies);
        await using var benHub = await HubDevice.ConnectAsync(setup.BaseUri, ben.Cookies);
        Assert.Contains(ana.Node, anaHub.ObservedNodes);
        Assert.Contains(ben.Node, benHub.ObservedNodes);

        var channel = Guid.Parse(setup.ChannelId);
        await JoinBothAsync(anaHub.Connection, benHub.Connection, channel);

        // Use the same HTTP send endpoint and transactional outbox used by the web application.
        await ExchangeAsync(ana, benHub, channel, expectBackplane);
        await ExchangeAsync(ben, anaHub, channel, expectBackplane);

        // A personal direct conversation is private to its participants and durable. PrivateChats
        // (the separate ephemeral feature) is deliberately not used in a multi-node test.
        var anaContacts = await ana.GetJsonAsync("/api/v1/contacts");
        var benContacts = await ben.GetJsonAsync("/api/v1/contacts");
        Assert.Contains(anaContacts.EnumerateArray(), item => item.GetProperty("user").GetProperty("id").GetGuid() == ben.UserId);
        Assert.Contains(benContacts.EnumerateArray(), item => item.GetProperty("user").GetProperty("id").GetGuid() == ana.UserId);
        var direct = await ana.PostJsonAsync("/api/v1/conversations/direct", new { userId = ben.UserId, organizationId = (Guid?)null });
        var directId = direct.GetProperty("id").GetGuid();
        await JoinBothAsync(anaHub.Connection, benHub.Connection, directId);
        await ExchangeAsync(ana, benHub, directId, expectBackplane);
        await ExchangeAsync(ben, anaHub, directId, expectBackplane);
    }

    [Fact]
    public async Task Client_reconnects_rejoins_and_catches_up_after_its_node_stops()
    {
        var setup = GetSetup();
        if (setup is null) return;
        if (string.Equals(Environment.GetEnvironmentVariable("CHATX_SCALEOUT_EXPECT_BACKPLANE"), "false", StringComparison.OrdinalIgnoreCase))
            Assert.Skip("Reconnect and catch-up acceptance requires the Redis backplane enabled.");
        if (!string.Equals(Environment.GetEnvironmentVariable("CHATX_SCALEOUT_TEST_FAILOVER"), "true", StringComparison.OrdinalIgnoreCase))
            Assert.Skip("Set CHATX_SCALEOUT_TEST_FAILOVER=true to let this test stop and restart one disposable staging API container.");

        await using var ana = await BrowserSession.SignInAsync(setup.BaseUri, setup.AnaEmail, setup.AnaPassword);
        await using var ben = await BrowserSession.SignInAsync(setup.BaseUri, setup.BenEmail, setup.BenPassword);
        Assert.NotEqual(ana.Node, ben.Node);
        var channel = Guid.Parse(setup.ChannelId);
        var survivor = ana.Node;
        var stopped = ben.Node;
        Assert.Contains(stopped, new[] { "api-a", "api-b" });
        Assert.Contains(survivor, new[] { "api-a", "api-b" });

        await using var anaHub = await HubDevice.ConnectAsync(setup.BaseUri, ana.Cookies);
        await using var benHub = await HubDevice.ConnectAsync(setup.BaseUri, ben.Cookies);
        await JoinBothAsync(anaHub.Connection, benHub.Connection, channel);

        var reconnecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejoined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        benHub.Connection.Reconnecting += _ =>
        {
            reconnecting.TrySetResult();
            return Task.CompletedTask;
        };
        benHub.Connection.Reconnected += async _ =>
        {
            try
            {
                await benHub.Connection.InvokeAsync(RealtimeContract.Methods.JoinConversation, channel);
                rejoined.TrySetResult();
            }
            catch (Exception ex)
            {
                rejoined.TrySetException(ex);
            }
        };

        await ComposeServiceAsync(setup, "stop", stopped);
        try
        {
            await reconnecting.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            var missedId = await ana.SendMessageAsync(channel, "scaleout missed durable message");
            await rejoined.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

            var history = await ben.GetJsonAsync($"/api/v1/conversations/{channel}/history?limit=100");
            Assert.Contains(history.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == missedId);
            Assert.Contains(survivor, benHub.ObservedNodes);

            var liveId = await ana.SendMessageAsync(channel, "scaleout live after reconnect");
            var live = await benHub.NextMessageAsync(channel, TimeSpan.FromSeconds(15));
            Assert.Equal(liveId, live.Entry.Id);
            Console.WriteLine($"Reconnect: Ben moved from {stopped} to {survivor}, rejoined the channel, found missed message {missedId} in durable history, and received {liveId} live.");
        }
        finally
        {
            await ComposeServiceAsync(setup, "start", stopped);
        }
    }

    private static async Task ExchangeAsync(BrowserSession sender, HubDevice receiver, Guid conversation, bool expectDelivery)
    {
        var expectedId = await sender.SendMessageAsync(conversation, $"scaleout {Guid.NewGuid():N}");
        if (expectDelivery)
        {
            var received = await receiver.NextMessageAsync(conversation, TimeSpan.FromSeconds(15));
            Assert.Equal(expectedId, received.Entry.Id);
            Console.WriteLine($"Delivered {received.Entry.Id} to the other node for conversation {conversation}.");
        }
        else
        {
            await receiver.AssertNoMessageAsync(conversation, TimeSpan.FromSeconds(5));
            Console.WriteLine($"Negative control: message {expectedId} was persisted but not delivered across nodes without the backplane.");
        }
    }

    private static async Task JoinBothAsync(HubConnection ana, HubConnection ben, Guid conversation)
    {
        await ana.InvokeAsync(RealtimeContract.Methods.JoinConversation, conversation);
        await ben.InvokeAsync(RealtimeContract.Methods.JoinConversation, conversation);
    }

    private static TestSetup? GetSetup()
    {
        var names = new[]
        {
            "CHATX_SCALEOUT_BASE_URL", "CHATX_SCALEOUT_CHANNEL_ID",
            "CHATX_SCALEOUT_ANA_EMAIL", "CHATX_SCALEOUT_ANA_PASSWORD",
            "CHATX_SCALEOUT_BEN_EMAIL", "CHATX_SCALEOUT_BEN_PASSWORD",
        };
        var missing = names.Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))).ToArray();
        if (missing.Length > 0)
        {
            Assert.Skip($"External two-node test not configured. Set: {string.Join(", ", missing)}.");
            return null;
        }

        return new TestSetup(
            new Uri(Environment.GetEnvironmentVariable("CHATX_SCALEOUT_BASE_URL")!),
            Environment.GetEnvironmentVariable("CHATX_SCALEOUT_CHANNEL_ID")!,
            Environment.GetEnvironmentVariable("CHATX_SCALEOUT_ANA_EMAIL")!,
            Environment.GetEnvironmentVariable("CHATX_SCALEOUT_ANA_PASSWORD")!,
            Environment.GetEnvironmentVariable("CHATX_SCALEOUT_BEN_EMAIL")!,
            Environment.GetEnvironmentVariable("CHATX_SCALEOUT_BEN_PASSWORD")!);
    }

    private static async Task ComposeServiceAsync(TestSetup setup, string action, string service)
    {
        var composeFile = Environment.GetEnvironmentVariable("CHATX_SCALEOUT_COMPOSE_FILE");
        var envFile = Environment.GetEnvironmentVariable("CHATX_SCALEOUT_ENV_FILE");
        if (string.IsNullOrWhiteSpace(composeFile) || string.IsNullOrWhiteSpace(envFile))
            throw new InvalidOperationException("Set CHATX_SCALEOUT_COMPOSE_FILE and CHATX_SCALEOUT_ENV_FILE to the disposable staging files before running failover acceptance.");

        var start = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "compose", "-f", Path.GetFullPath(composeFile), "--env-file", Path.GetFullPath(envFile), action, service })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start docker compose.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker compose {action} {service} failed: {output} {error}");
    }

    private sealed record TestSetup(Uri BaseUri, string ChannelId, string AnaEmail, string AnaPassword, string BenEmail, string BenPassword);

    private sealed class BrowserSession : IAsyncDisposable
    {
        private readonly HttpClient _http;
        private string _csrf = string.Empty;

        private BrowserSession(HttpClient http, CookieContainer cookies)
        {
            _http = http;
            Cookies = cookies;
        }

        public CookieContainer Cookies { get; }
        public Guid UserId { get; private set; }
        public string Node { get; private set; } = string.Empty;

        public static async Task<BrowserSession> SignInAsync(Uri baseUri, string email, string password)
        {
            var cookies = new CookieContainer();
            var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true };
            var http = new HttpClient(handler) { BaseAddress = baseUri };
            var session = new BrowserSession(http, cookies);
            try
            {
                await session.RefreshCsrfAsync();
                await session.PostJsonAsync("/api/v1/auth/login", new { email, password, rememberMe = false });
                var node = cookies.GetCookies(baseUri)["CHATX_NODE"]?.Value;
                if (string.IsNullOrWhiteSpace(node))
                    throw new InvalidOperationException("HAProxy did not assign its CHATX_NODE affinity cookie.");
                await session.RefreshCsrfAsync();
                var me = await session.GetJsonAsync("/api/v1/users/me");
                session.UserId = me.GetProperty("id").GetGuid();
                session.Node = node;
                return session;
            }
            catch
            {
                http.Dispose();
                throw;
            }
        }

        public async Task<JsonElement> GetJsonAsync(string path)
        {
            using var response = await _http.GetAsync(path);
            return await ReadJsonAsync(response);
        }

        public async Task<JsonElement> PostJsonAsync(string path, object body)
        {
            if (string.IsNullOrEmpty(_csrf)) await RefreshCsrfAsync();
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-CSRF-TOKEN", _csrf);
            using var response = await _http.SendAsync(request);
            return await ReadJsonAsync(response);
        }

        public async Task<Guid> SendMessageAsync(Guid conversationId, string body)
        {
            var created = await PostJsonAsync($"/api/v1/conversations/{conversationId}/messages", new
            {
                clientMessageId = Guid.NewGuid().ToString("N"), body, replyToId = (Guid?)null,
            });
            return created.GetProperty("id").GetGuid();
        }

        private async Task RefreshCsrfAsync()
        {
            using var response = await _http.GetAsync("/api/v1/auth/csrf");
            var token = await ReadJsonAsync(response);
            _csrf = token.GetProperty("token").GetString()!;
        }

        public ValueTask DisposeAsync()
        {
            _http.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HubDevice : IAsyncDisposable
    {
        private readonly Channel<MessageEvent> _messages = Channel.CreateUnbounded<MessageEvent>();
        private readonly ConcurrentQueue<string> _nodes;

        private HubDevice(HubConnection connection, ConcurrentQueue<string> nodes)
        {
            Connection = connection;
            _nodes = nodes;
            connection.On<MessageEvent>(RealtimeContract.Events.MessageCreated, message => _messages.Writer.TryWrite(message));
        }

        public HubConnection Connection { get; }
        public IReadOnlyCollection<string> ObservedNodes => _nodes.ToArray();

        public static async Task<HubDevice> ConnectAsync(Uri baseUri, CookieContainer cookies)
        {
            var uri = new Uri(baseUri, RealtimeContract.HubPath);
            var nodes = new ConcurrentQueue<string>();
            var connection = new HubConnectionBuilder()
                .WithUrl(uri, options =>
                {
                    options.Cookies = cookies;
                    options.HttpMessageHandlerFactory = inner => new NodeCaptureHandler(inner, node => { if (node is not null) nodes.Enqueue(node); });
                })
                .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)])
                .Build();
            var result = new HubDevice(connection, nodes);
            await connection.StartAsync();
            await connection.InvokeAsync(RealtimeContract.Methods.GetPresence);
            return result;
        }

        public async Task<MessageEvent> NextMessageAsync(Guid conversationId, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (true)
            {
                var message = await _messages.Reader.ReadAsync(cancellation.Token);
                if (message.ConversationId == conversationId) return message;
            }
        }

        public async Task AssertNoMessageAsync(Guid conversationId, TimeSpan duration)
        {
            using var cancellation = new CancellationTokenSource(duration);
            try
            {
                while (true)
                {
                    var message = await _messages.Reader.ReadAsync(cancellation.Token);
                    Assert.NotEqual(conversationId, message.ConversationId);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
    }

    private sealed class NodeCaptureHandler(HttpMessageHandler inner, Action<string?> observed) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("X-ChatX-Node", out var values)) observed(values.FirstOrDefault());
            return response;
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} returned {(int)response.StatusCode}: {content}");
        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} returned {(int)response.StatusCode}: {content}");
        }
    }
}
