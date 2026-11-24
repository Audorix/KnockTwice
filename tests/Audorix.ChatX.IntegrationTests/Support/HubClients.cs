using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Audorix.ChatX.Application.Realtime;
using Audorix.ChatX.Contracts.V1.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;

namespace Audorix.ChatX.IntegrationTests.Support;

/// <summary>A signed-in device: its own sign-in session (cookie) and a hub connection that records server events.</summary>
public sealed class HubDevice : IAsyncDisposable
{
    private readonly Channel<UserPresenceChangedEvent> _presence = Channel.CreateUnbounded<UserPresenceChangedEvent>();
    private readonly Channel<TypingStatusChangedEvent> _typing = Channel.CreateUnbounded<TypingStatusChangedEvent>();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private HubDevice(ApiSession session, HubConnection hub)
    {
        Session = session;
        Hub = hub;
        hub.On<UserPresenceChangedEvent>(RealtimeContract.Events.UserPresenceChanged, e => _presence.Writer.TryWrite(e));
        hub.On<TypingStatusChangedEvent>(RealtimeContract.Events.TypingStatusChanged, e => _typing.Writer.TryWrite(e));
        hub.Closed += _ =>
        {
            _closed.TrySetResult();
            return Task.CompletedTask;
        };
    }

    public ApiSession Session { get; }

    public HubConnection Hub { get; }

    public Task Closed => _closed.Task;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Signs in (a new session, like a new device) and connects to the hub with that session's cookie.</summary>
    public static async Task<HubDevice> ConnectAsync(ChatXApiFactory api, string email, bool start = true)
    {
        var session = new ApiSession(api.CreateHttpsClient());
        var login = await session.LoginAsync(email, TestUsers.Password);
        AssertStatus(HttpStatusCode.OK, login);
        var cookie = login.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("chatx.auth=", StringComparison.Ordinal))
            .Split(';')[0];

        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri("https://localhost" + RealtimeContract.HubPath), options =>
            {
                options.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers["Cookie"] = cookie;
            })
            .Build();
        var device = new HubDevice(session, hub);
        if (start)
        {
            await hub.StartAsync(Ct);

            // StartAsync returns after the handshake, possibly before the hub's OnConnectedAsync has
            // registered the connection; events sent in between (private chats address registered
            // connections) would be missed. The server answers invocations only after
            // OnConnectedAsync, so one harmless call waits for the registration.
            await hub.InvokeAsync<IReadOnlyList<UserPresenceResponse>>(RealtimeContract.Methods.GetPresence, Ct);
        }

        return device;
    }

    public async Task<UserPresenceChangedEvent> NextPresenceAsync() => await Next(_presence);

    public async Task<TypingStatusChangedEvent> NextTypingAsync() => await Next(_typing);

    public Task AssertNoPresenceAsync(int milliseconds = 700) => AssertNone(_presence, milliseconds);

    public Task AssertNoTypingAsync(int milliseconds = 700) => AssertNone(_typing, milliseconds);

    public async ValueTask DisposeAsync()
    {
        await Hub.DisposeAsync();
        Session.Dispose();
    }

    private static async Task<T> Next<T>(Channel<T> channel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        return await channel.Reader.ReadAsync(timeout.Token);
    }

    private static async Task AssertNone<T>(Channel<T> channel, int milliseconds)
    {
        await Task.Delay(milliseconds, Ct);
        Assert.False(channel.Reader.TryRead(out var unexpected), $"Unexpected event: {unexpected}");
    }
}

/// <summary>
/// The protected Phase 4 fixture: conversation access granted explicitly by the test. Production
/// uses <see cref="NoConversationAccess"/> until conversations exist (Phase 5).
/// </summary>
public sealed class FixtureConversationAccess : IConversationAccess
{
    private readonly ConcurrentDictionary<(Guid UserId, Guid ConversationId), bool> _grants = new();

    public void Grant(Guid conversationId, params Guid[] userIds)
    {
        foreach (var userId in userIds)
        {
            _grants[(userId, conversationId)] = true;
        }
    }

    public void Revoke(Guid conversationId, Guid userId) => _grants.TryRemove((userId, conversationId), out _);

    public Task<bool> CanAccessAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        Task.FromResult(_grants.ContainsKey((userId, conversationId)));
}
