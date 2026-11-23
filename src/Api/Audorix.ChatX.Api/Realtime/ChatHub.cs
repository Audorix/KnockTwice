using System.Collections.Concurrent;
using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Realtime;
using Audorix.ChatX.Contracts.V1.Realtime;
using Audorix.ChatX.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Audorix.ChatX.Api.Realtime;

/// <summary>Server-to-client events of <c>/hubs/chat</c> (see <see cref="RealtimeContract"/>).</summary>
public interface IChatClient
{
    Task UserPresenceChanged(UserPresenceChangedEvent payload);

    Task TypingStatusChanged(TypingStatusChangedEvent payload);

    Task ConversationUpdated(ConversationUpdatedEvent payload);

    Task MessageCreated(MessageEvent payload);

    Task MessageUpdated(MessageEvent payload);

    Task MessageDeleted(MessageEvent payload);

    Task MessageDelivered(ReceiptEvent payload);

    Task MessageRead(ReceiptEvent payload);

    Task PrivateChatChanged(PrivateChatChangedEvent payload);

    Task PrivateMessageCreated(PrivateMessageEvent payload);

    /// <summary>Phase 16: a message's reactions changed (full new set).</summary>
    Task ReactionChanged(ReactionChangedEvent payload);

    /// <summary>Phase 20: the user's drafts, scheduled messages or reminders changed.</summary>
    Task PlanningChanged(PlanningChangedEvent payload);

    /// <summary>Phase 20: a reminder came due.</summary>
    Task ReminderDue(ReminderDueEvent payload);
}

/// <summary>
/// The realtime endpoint. Connections require the sign-in cookie; every invocation re-checks the
/// session (<see cref="RealtimeHubFilter"/>) and every destination operation re-checks access in
/// <see cref="RealtimeService"/>. The hub is a thin adapter: it holds no state and no rules.
/// </summary>
[Authorize]
internal sealed class ChatHub(RealtimeService realtime, SignalRConnections connections) : Hub<IChatClient>
{
    public override async Task OnConnectedAsync()
    {
        connections.Track(Context);
        await realtime.ConnectedAsync(Context.ConnectionId, Context.User!.RequireUserId(), Context.User!.RequireSessionId(), Context.ConnectionAborted)
            .ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Untrack(Context.ConnectionId);
        await realtime.DisconnectedAsync(Context.ConnectionId, CancellationToken.None).ConfigureAwait(false);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UserPresenceResponse>> GetPresence()
    {
        var presence = await realtime.GetVisiblePresenceAsync(Context.User!.RequireUserId(), Context.ConnectionAborted).ConfigureAwait(false);
        return presence.Select(p => new UserPresenceResponse(p.UserId, SignalRRealtimePublisher.Format(p.Status))).ToList();
    }

    public Task JoinConversation(Guid conversationId) =>
        realtime.JoinConversationAsync(Context.ConnectionId, Context.User!.RequireUserId(), conversationId, Context.ConnectionAborted);

    public Task LeaveConversation(Guid conversationId) =>
        realtime.LeaveConversationAsync(Context.ConnectionId, Context.User!.RequireUserId(), conversationId, Context.ConnectionAborted);

    public Task SendTypingStatus(Guid conversationId, bool isTyping) =>
        realtime.SetTypingAsync(Context.ConnectionId, Context.User!.RequireUserId(), conversationId, isTyping, Context.ConnectionAborted);
}

/// <summary>
/// Re-validates the connection's sign-in session on every invocation and maps application
/// failures to <see cref="HubException"/> messages carrying stable error codes only.
/// </summary>
internal sealed class RealtimeHubFilter : IHubFilter
{
    public const string Unauthorized = "unauthorized";

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var context = invocationContext.Context;
        var active = invocationContext.Hub is Widgets.WidgetHub
            ? Widgets.VisitorAuthenticationHandler.TryGetVisitor(context.User!) is { } visitor
                && await invocationContext.ServiceProvider.GetRequiredService<Application.Widgets.VisitorService>()
                    .IsSessionActiveAsync(visitor.SessionId, context.ConnectionAborted).ConfigureAwait(false)
            : await invocationContext.ServiceProvider.GetRequiredService<RealtimeService>()
                .IsSessionActiveAsync(context.ConnectionId, context.ConnectionAborted).ConfigureAwait(false);
        if (!active)
        {
            context.Abort();
            throw new HubException(Unauthorized);
        }

        try
        {
            return await next(invocationContext).ConfigureAwait(false);
        }
        catch (ApplicationRuleException ex)
        {
            throw new HubException(ex.Code);
        }
        catch (DomainRuleException ex)
        {
            throw new HubException(ex.Code);
        }
    }
}

/// <summary>Maps application events to the versioned wire contracts.</summary>
internal sealed class SignalRRealtimePublisher(IHubContext<ChatHub, IChatClient> hub, IHubContext<Widgets.WidgetHub, IChatClient> widgetHub) : IRealtimePublisher
{
    public static string Format(PresenceStatus status) => status == PresenceStatus.Online ? "online" : "offline";

    public async Task PublishToUsersAsync(IReadOnlyCollection<Guid> userIds, IRealtimeEvent realtimeEvent, CancellationToken cancellationToken)
    {
        // The default user id provider reads the NameIdentifier claim, which holds the user id
        // (for visitors, their stand-in account). Presence and private chats never reach visitors.
        var ids = userIds.Select(id => id.ToString()).ToList();
        await Send(hub.Clients.Users(ids), realtimeEvent).ConfigureAwait(false);
        if (ReachesVisitors(realtimeEvent))
        {
            await Send(widgetHub.Clients.Users(ids), realtimeEvent).ConfigureAwait(false);
        }
    }

    public async Task PublishToConversationAsync(Guid conversationId, IRealtimeEvent realtimeEvent, CancellationToken cancellationToken)
    {
        var group = SignalRConnections.ConversationGroup(conversationId);
        await Send(hub.Clients.Group(group), realtimeEvent).ConfigureAwait(false);
        if (ReachesVisitors(realtimeEvent))
        {
            await Send(widgetHub.Clients.Group(group), realtimeEvent).ConfigureAwait(false);
        }
    }

    private static bool ReachesVisitors(IRealtimeEvent realtimeEvent) =>
        realtimeEvent is not (UserPresenceChanged or Application.PrivateChats.PrivateChatChanged or Application.PrivateChats.PrivateMessageCreated
            or Application.Messaging.ReactionChanged or Application.Planning.PlanningChanged or Application.Planning.ReminderDue);

    public Task PublishToConnectionsAsync(IReadOnlyCollection<string> connectionIds, IRealtimeEvent realtimeEvent, CancellationToken cancellationToken) =>
        connectionIds.Count == 0 ? Task.CompletedTask : Send(hub.Clients.Clients(connectionIds.ToList()), realtimeEvent);

    private static Task Send(IChatClient clients, IRealtimeEvent realtimeEvent) => realtimeEvent switch
    {
        UserPresenceChanged e => clients.UserPresenceChanged(new UserPresenceChangedEvent(RealtimeContract.Version, e.UserId, Format(e.Status), e.AtUtc)),
        TypingStatusChanged e => clients.TypingStatusChanged(new TypingStatusChangedEvent(RealtimeContract.Version, e.ConversationId, e.UserId, e.IsTyping, e.ExpiresAtUtc)),
        Application.Conversations.ConversationUpdated e => clients.ConversationUpdated(
            new ConversationUpdatedEvent(RealtimeContract.Version, e.ConversationId, e.Change, e.LastSequence)),
        Application.Messaging.MessageChanged e => SendMessage(clients, e),
        Application.Messaging.ReceiptChanged { Kind: "delivered" } e => clients.MessageDelivered(new ReceiptEvent(RealtimeContract.Version, e.ConversationId, e.UserId, e.Sequence)),
        Application.Messaging.ReceiptChanged e => clients.MessageRead(new ReceiptEvent(RealtimeContract.Version, e.ConversationId, e.UserId, e.Sequence)),
        Application.PrivateChats.PrivateChatChanged e => clients.PrivateChatChanged(new PrivateChatChangedEvent(RealtimeContract.Version, e.ChatId, e.Status, e.Reason)),
        Application.PrivateChats.PrivateMessageCreated e => clients.PrivateMessageCreated(
            new PrivateMessageEvent(RealtimeContract.Version, e.ChatId, Endpoints.PrivateChatEndpoints.ToMessage(e.Message))),
        Application.Messaging.ReactionChanged e => clients.ReactionChanged(new ReactionChangedEvent(RealtimeContract.Version, e.ConversationId, e.MessageId,
            e.Reactions.Select(Endpoints.ConversationEndpoints.ToReaction).ToList())),
        Application.Planning.PlanningChanged e => clients.PlanningChanged(new PlanningChangedEvent(RealtimeContract.Version, e.Kind)),
        Application.Planning.ReminderDue e => clients.ReminderDue(
            new ReminderDueEvent(RealtimeContract.Version, e.ReminderId, e.ConversationId, e.MessageId, e.ThreadRootId, e.AuthorName, e.Excerpt)),
        _ => throw new NotSupportedException($"No wire contract for {realtimeEvent.GetType().Name}."),
    };

    private static Task SendMessage(IChatClient clients, Application.Messaging.MessageChanged e)
    {
        var payload = new MessageEvent(RealtimeContract.Version, e.EventId, e.Message.Entry.ConversationId, Endpoints.ConversationEndpoints.ToEntry(e.Message));
        return e.Type switch
        {
            Application.Messaging.OutboxMessage.MessageCreated => clients.MessageCreated(payload),
            Application.Messaging.OutboxMessage.MessageDeleted => clients.MessageDeleted(payload),
            _ => clients.MessageUpdated(payload),
        };
    }
}

/// <summary>SignalR group membership and the open connections of this node, so revoked sessions can be closed.</summary>
internal sealed class SignalRConnections(IHubContext<ChatHub, IChatClient> hub) : IRealtimeConnections
{
    private readonly ConcurrentDictionary<string, HubCallerContext> _open = new(StringComparer.Ordinal);

    public static string ConversationGroup(Guid conversationId) => $"conversation:{conversationId:N}";

    public void Track(HubCallerContext context) => _open[context.ConnectionId] = context;

    public void Untrack(string connectionId) => _open.TryRemove(connectionId, out _);

    public Task AddToConversationAsync(Guid conversationId, string connectionId, CancellationToken cancellationToken) =>
        hub.Groups.AddToGroupAsync(connectionId, ConversationGroup(conversationId), cancellationToken);

    public async Task RemoveFromConversationAsync(Guid conversationId, IReadOnlyCollection<string> connectionIds, CancellationToken cancellationToken)
    {
        foreach (var connectionId in connectionIds)
        {
            await hub.Groups.RemoveFromGroupAsync(connectionId, ConversationGroup(conversationId), cancellationToken).ConfigureAwait(false);
        }
    }

    public void Abort(IReadOnlyCollection<string> connectionIds)
    {
        foreach (var connectionId in connectionIds)
        {
            if (_open.TryGetValue(connectionId, out var context))
            {
                context.Abort();
            }
        }
    }
}
