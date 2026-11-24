using System.Collections.Concurrent;
using Audorix.ChatX.Api.Realtime;
using Audorix.ChatX.Application.Messaging;
using Audorix.ChatX.Application.Realtime;
using Audorix.ChatX.Application.Widgets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Api.Widgets;

/// <summary>
/// Realtime endpoint of the embedded chat (<c>/hubs/widget</c>). A visitor connection only ever
/// joins the visitor's own conversation; it receives the same events as <c>/hubs/chat</c> for that
/// conversation (messages, receipts, typing) and nothing else. The session is re-checked on every
/// invocation (<see cref="RealtimeHubFilter"/>) and periodically (<see cref="WidgetConnectionSweeper"/>).
/// </summary>
[Authorize(Policy = VisitorAuthenticationHandler.PolicyName)]
internal sealed class WidgetHub(VisitorService visitors, WidgetConnections connections, IRealtimePublisher publisher, TimeProvider time)
    : Hub<IChatClient>
{
    private static readonly TimeSpan TypingLifetime = TimeSpan.FromSeconds(6);

    public override async Task OnConnectedAsync()
    {
        var visitor = VisitorAuthenticationHandler.RequireVisitor(Context.User!);
        connections.Track(Context, visitor.SessionId);
        await JoinConversation().ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Untrack(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Joins the visitor's conversation (call again after the first message created it). Returns its id, if any.</summary>
    public async Task<Guid?> JoinConversation()
    {
        var visitor = VisitorAuthenticationHandler.RequireVisitor(Context.User!);
        var view = await visitors.GetConversationIdAsync(visitor, Context.ConnectionAborted).ConfigureAwait(false);
        if (view is { } conversationId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SignalRConnections.ConversationGroup(conversationId), Context.ConnectionAborted).ConfigureAwait(false);
        }

        return view;
    }

    public async Task SendTypingStatus(bool isTyping)
    {
        var visitor = VisitorAuthenticationHandler.RequireVisitor(Context.User!);
        if (await visitors.GetConversationIdAsync(visitor, Context.ConnectionAborted).ConfigureAwait(false) is { } conversationId)
        {
            var expires = isTyping ? time.GetUtcNow() + TypingLifetime : (DateTimeOffset?)null;
            await publisher.PublishToConversationAsync(conversationId, new TypingStatusChanged(conversationId, visitor.UserId, isTyping, expires), Context.ConnectionAborted)
                .ConfigureAwait(false);
        }
    }
}

/// <summary>Open visitor connections of this node, so ended sessions and disabled widgets can be disconnected.</summary>
internal sealed class WidgetConnections
{
    private readonly ConcurrentDictionary<string, (HubCallerContext Context, Guid SessionId)> _open = new(StringComparer.Ordinal);

    public void Track(HubCallerContext context, Guid sessionId) => _open[context.ConnectionId] = (context, sessionId);

    public void Untrack(string connectionId) => _open.TryRemove(connectionId, out _);

    public IReadOnlyList<(HubCallerContext Context, Guid SessionId)> Snapshot() => _open.Values.ToList();
}

/// <summary>Closes visitor connections whose session ended or whose widget was switched off.</summary>
internal sealed class WidgetConnectionSweeper(
    IServiceScopeFactory scopes, WidgetConnections connections, IOptions<RealtimeSettings> settings, TimeProvider time, ILogger<WidgetConnectionSweeper> logger)
    : Hosting.QuietBackgroundService
{
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.Value.SessionCheckInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var open = connections.Snapshot();
                if (open.Count == 0)
                {
                    continue;
                }

                await using var scope = scopes.CreateAsyncScope();
                var visitors = scope.ServiceProvider.GetRequiredService<VisitorService>();
                foreach (var group in open.GroupBy(c => c.SessionId))
                {
                    if (!await visitors.IsSessionActiveAsync(group.Key, stoppingToken).ConfigureAwait(false))
                    {
                        foreach (var (context, _) in group)
                        {
                            context.Abort();
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug("Widget connection check skipped ({ExceptionType}).", ex.GetType().Name);
            }
        }
    }
}
