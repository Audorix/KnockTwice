using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Sessions;

namespace Audorix.ChatX.Application.Realtime;

/// <summary>Application events delivered through <see cref="IRealtimePublisher"/>. The API maps them to versioned wire contracts.</summary>
public interface IRealtimeEvent;

public sealed record UserPresenceChanged(Guid UserId, PresenceStatus Status, DateTimeOffset AtUtc) : IRealtimeEvent;

public sealed record TypingStatusChanged(Guid ConversationId, Guid UserId, bool IsTyping, DateTimeOffset? ExpiresAtUtc) : IRealtimeEvent;

/// <summary>Transport port (ADR-005): application code never references SignalR.</summary>
public interface IRealtimePublisher
{
    /// <summary>Delivers to every connection of the given users.</summary>
    Task PublishToUsersAsync(IReadOnlyCollection<Guid> userIds, IRealtimeEvent realtimeEvent, CancellationToken cancellationToken);

    /// <summary>Delivers to connections that joined the conversation (an authorized destination, not a security boundary).</summary>
    Task PublishToConversationAsync(Guid conversationId, IRealtimeEvent realtimeEvent, CancellationToken cancellationToken);

    /// <summary>Delivers to specific connections (for example only the connections of one sign-in session).</summary>
    Task PublishToConnectionsAsync(IReadOnlyCollection<string> connectionIds, IRealtimeEvent realtimeEvent, CancellationToken cancellationToken);
}

/// <summary>Transport-level destination membership and connection control.</summary>
public interface IRealtimeConnections
{
    Task AddToConversationAsync(Guid conversationId, string connectionId, CancellationToken cancellationToken);

    Task RemoveFromConversationAsync(Guid conversationId, IReadOnlyCollection<string> connectionIds, CancellationToken cancellationToken);

    /// <summary>Closes connections, for example after their sign-in session was revoked.</summary>
    void Abort(IReadOnlyCollection<string> connectionIds);
}

/// <summary>Who may see a user's presence: people with a relationship (contacts or a shared active organization), never across a block. Symmetric.</summary>
public interface IPresenceAudience
{
    Task<IReadOnlyList<Guid>> GetAudienceAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Conversation access check for realtime destinations, re-evaluated on every protected
/// operation (connection-time authorization alone is insufficient, Architecture §11).
/// Implemented over conversation participation and organization membership (Phase 5).
/// </summary>
public interface IConversationAccess
{
    Task<bool> CanAccessAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);
}

public sealed record UserPresence(Guid UserId, PresenceStatus Status);

/// <summary>
/// Realtime use cases behind the hub: connection lifecycle, presence, conversation
/// destinations and typing indicators. Every destination operation re-checks access.
/// </summary>
public sealed class RealtimeService(
    RealtimeState state,
    IPresenceAudience audience,
    IConversationAccess access,
    IRealtimePublisher publisher,
    IRealtimeConnections connections,
    SessionService sessions,
    TimeProvider time,
    IClusterPresence cluster)
{
    public const string ConversationNotFound = "conversation_not_found";

    public async Task ConnectedAsync(string connectionId, Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (state.Connect(connectionId, userId, sessionId))
        {
            // Phase 21: with several instances, someone already online elsewhere is not announced again.
            var elsewhere = (await cluster.OnlineElsewhereAsync([userId], cancellationToken).ConfigureAwait(false)).Contains(userId);
            await cluster.MarkOnlineAsync(userId, cancellationToken).ConfigureAwait(false);
            if (!elsewhere)
            {
                await PublishPresenceAsync(userId, PresenceStatus.Online, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task DisconnectedAsync(string connectionId, CancellationToken cancellationToken)
    {
        var result = state.Disconnect(connectionId);
        foreach (var conversationId in result.TypingStoppedIn)
        {
            await publisher.PublishToConversationAsync(conversationId, new TypingStatusChanged(conversationId, result.UserId!.Value, false, null), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Presence of the people the user may see (their audience is symmetric), online first.</summary>
    public async Task<IReadOnlyList<UserPresence>> GetVisiblePresenceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var visible = await audience.GetAudienceAsync(userId, cancellationToken).ConfigureAwait(false);
        var online = state.OnlineAmong(visible).ToHashSet();
        var rest = visible.Where(id => !online.Contains(id)).ToList();
        if (rest.Count > 0)
        {
            online.UnionWith(await cluster.OnlineElsewhereAsync(rest, cancellationToken).ConfigureAwait(false));
        }
        return visible
            .Select(id => new UserPresence(id, online.Contains(id) ? PresenceStatus.Online : PresenceStatus.Offline))
            .OrderByDescending(p => p.Status)
            .ToList();
    }

    public async Task JoinConversationAsync(string connectionId, Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        if (!await access.CanAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false))
        {
            throw new ResourceNotFoundException(ConversationNotFound);
        }

        // Joining again (for example after a reconnect) is harmless: group membership is a set.
        state.Join(connectionId, conversationId);
        await connections.AddToConversationAsync(conversationId, connectionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task LeaveConversationAsync(string connectionId, Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var typingStopped = state.Leave(connectionId, conversationId);
        await connections.RemoveFromConversationAsync(conversationId, [connectionId], cancellationToken).ConfigureAwait(false);
        if (typingStopped)
        {
            await publisher.PublishToConversationAsync(conversationId, new TypingStatusChanged(conversationId, userId, false, null), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task SetTypingAsync(string connectionId, Guid userId, Guid conversationId, bool isTyping, CancellationToken cancellationToken)
    {
        if (!state.HasJoined(connectionId, conversationId))
        {
            throw new ResourceNotFoundException(ConversationNotFound);
        }

        if (!await access.CanAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false))
        {
            await RevokeConversationAccessAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
            throw new ResourceNotFoundException(ConversationNotFound);
        }

        var (changed, expires) = state.SetTyping(conversationId, userId, isTyping);
        if (changed)
        {
            await publisher.PublishToConversationAsync(conversationId, new TypingStatusChanged(conversationId, userId, isTyping, expires), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Removes the user's connections from a conversation they may no longer access, so nothing
    /// protected is delivered to an open socket afterwards. Called when membership is revoked.
    /// </summary>
    public async Task RevokeConversationAccessAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var (connectionIds, typingStopped) = state.RemoveUserFromConversation(userId, conversationId);
        if (connectionIds.Count > 0)
        {
            await connections.RemoveFromConversationAsync(conversationId, connectionIds, cancellationToken).ConfigureAwait(false);
        }

        if (typingStopped)
        {
            await publisher.PublishToConversationAsync(conversationId, new TypingStatusChanged(conversationId, userId, false, null), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Publishes due offline transitions and expired typing indicators.</summary>
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        var result = state.Sweep();
        if (result.WentOffline.Count > 0)
        {
            foreach (var userId in result.WentOffline)
            {
                await cluster.MarkOfflineAsync(userId, cancellationToken).ConfigureAwait(false);
            }

            // Phase 21: someone still connected to another instance stays online.
            var elsewhere = await cluster.OnlineElsewhereAsync(result.WentOffline, cancellationToken).ConfigureAwait(false);
            foreach (var userId in result.WentOffline.Where(id => !elsewhere.Contains(id)))
            {
                await PublishPresenceAsync(userId, PresenceStatus.Offline, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var (conversationId, userId) in result.TypingExpired)
        {
            await publisher.PublishToConversationAsync(conversationId, new TypingStatusChanged(conversationId, userId, false, null), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Renews this instance's shared presence entries (Phase 21; nothing to do on one server).</summary>
    public Task RefreshClusterPresenceAsync(CancellationToken cancellationToken) => cluster.RefreshAsync(state.OnlineUsers(), cancellationToken);

    /// <summary>True when the connection's sign-in session is still active.</summary>
    public async Task<bool> IsSessionActiveAsync(string connectionId, CancellationToken cancellationToken)
    {
        var connection = state.Find(connectionId);
        return connection is not null
            && await sessions.ValidateAsync(connection.SessionId, connection.UserId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes connections whose session was revoked or expired. Returns how many were closed.</summary>
    public async Task<int> CloseRevokedSessionsAsync(CancellationToken cancellationToken)
    {
        var closing = new List<string>();
        foreach (var group in state.Snapshot().GroupBy(c => (c.SessionId, c.UserId)))
        {
            if (!await sessions.ValidateAsync(group.Key.SessionId, group.Key.UserId, cancellationToken).ConfigureAwait(false))
            {
                closing.AddRange(group.Select(c => c.ConnectionId));
            }
        }

        if (closing.Count > 0)
        {
            connections.Abort(closing);
        }

        return closing.Count;
    }

    /// <summary>Closes the connections of one session immediately (sign-out or revocation).</summary>
    public void CloseSession(Guid sessionId)
    {
        var closing = state.Snapshot().Where(c => c.SessionId == sessionId).Select(c => c.ConnectionId).ToList();
        if (closing.Count > 0)
        {
            connections.Abort(closing);
        }
    }

    private async Task PublishPresenceAsync(Guid userId, PresenceStatus status, CancellationToken cancellationToken)
    {
        var watchers = await audience.GetAudienceAsync(userId, cancellationToken).ConfigureAwait(false);
        if (watchers.Count > 0)
        {
            await publisher.PublishToUsersAsync(watchers, new UserPresenceChanged(userId, status, time.GetUtcNow()), cancellationToken).ConfigureAwait(false);
        }
    }
}
