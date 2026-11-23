using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Realtime;

/// <summary>Realtime timing policy bound from the <c>Realtime</c> configuration section.</summary>
public sealed class RealtimeSettings
{
    public const string SectionName = "Realtime";

    /// <summary>How long a user stays "online" after their last connection closes, so reloads and short reconnects cause no flicker.</summary>
    public TimeSpan PresenceOfflineGrace { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>A typing indicator expires this long after the last "typing" signal.</summary>
    public TimeSpan TypingTimeout { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>How often expired typing indicators and offline transitions are processed.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How often open connections are checked against their sign-in session (revoked sessions are disconnected).</summary>
    public TimeSpan SessionCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (PresenceOfflineGrace < TimeSpan.Zero || PresenceOfflineGrace > TimeSpan.FromMinutes(5))
        {
            errors.Add("Realtime:PresenceOfflineGrace must be between 0 and 5 minutes.");
        }

        if (TypingTimeout < TimeSpan.FromMilliseconds(100) || TypingTimeout > TimeSpan.FromMinutes(1))
        {
            errors.Add("Realtime:TypingTimeout must be between 100 milliseconds and 1 minute.");
        }

        if (SweepInterval < TimeSpan.FromMilliseconds(50) || SweepInterval > TimeSpan.FromSeconds(10))
        {
            errors.Add("Realtime:SweepInterval must be between 50 milliseconds and 10 seconds.");
        }

        if (SessionCheckInterval < TimeSpan.FromSeconds(1) || SessionCheckInterval > TimeSpan.FromHours(1))
        {
            errors.Add("Realtime:SessionCheckInterval must be between 1 second and 1 hour.");
        }

        return errors;
    }
}

public enum PresenceStatus
{
    Offline,
    Online,
}

public sealed record RealtimeConnection(string ConnectionId, Guid UserId, Guid SessionId);

/// <summary>Outcome of <see cref="RealtimeState.Disconnect"/>: typing indicators that ended with the connection.</summary>
public sealed record DisconnectResult(Guid? UserId, IReadOnlyList<Guid> TypingStoppedIn);

public sealed record SweepResult(IReadOnlyList<Guid> WentOffline, IReadOnlyList<(Guid ConversationId, Guid UserId)> TypingExpired);

/// <summary>
/// Process-local, ephemeral realtime state (Architecture §8, §17: acceptable for a single node;
/// a distributed coordinator is required for scale-out). Tracks every connection of every user
/// (multiple devices), the conversations each connection joined, presence with an offline grace
/// period, and typing indicators that expire. Nothing here is persisted. All members are
/// thread-safe; callers publish the returned changes outside the lock.
/// </summary>
public sealed class RealtimeState(TimeProvider time, IOptions<RealtimeSettings> settings)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _connections = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, HashSet<string>> _byUser = [];
    private readonly Dictionary<Guid, DateTimeOffset> _offlineDue = [];
    private readonly Dictionary<(Guid ConversationId, Guid UserId), DateTimeOffset> _typing = [];

    /// <summary>Registers a connection. True when the user was offline (a presence change to publish).</summary>
    public bool Connect(string connectionId, Guid userId, Guid sessionId)
    {
        lock (_gate)
        {
            var wasOnline = IsOnlineLocked(userId);
            _connections[connectionId] = new Entry(userId, sessionId);
            if (!_byUser.TryGetValue(userId, out var set))
            {
                _byUser[userId] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            set.Add(connectionId);
            _offlineDue.Remove(userId);
            return !wasOnline;
        }
    }

    /// <summary>
    /// Removes a connection. The user goes offline only after the grace period without any
    /// connection (see <see cref="Sweep"/>). Typing ends in conversations the user no longer
    /// has a joined connection for.
    /// </summary>
    public DisconnectResult Disconnect(string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.Remove(connectionId, out var entry))
            {
                return new DisconnectResult(null, []);
            }

            var remaining = _byUser[entry.UserId];
            remaining.Remove(connectionId);
            if (remaining.Count == 0)
            {
                _byUser.Remove(entry.UserId);
                _offlineDue[entry.UserId] = time.GetUtcNow() + settings.Value.PresenceOfflineGrace;
            }

            var stopped = new List<Guid>();
            foreach (var conversationId in entry.Conversations)
            {
                if (!HasJoinedLocked(entry.UserId, conversationId) && _typing.Remove((conversationId, entry.UserId)))
                {
                    stopped.Add(conversationId);
                }
            }

            return new DisconnectResult(entry.UserId, stopped);
        }
    }

    public bool IsOnline(Guid userId)
    {
        lock (_gate)
        {
            return IsOnlineLocked(userId);
        }
    }

    public IReadOnlyList<Guid> OnlineAmong(IEnumerable<Guid> userIds)
    {
        lock (_gate)
        {
            return userIds.Where(IsOnlineLocked).Distinct().ToList();
        }
    }

    /// <summary>Users online on this instance, including those still in their offline grace period.</summary>
    public IReadOnlyList<Guid> OnlineUsers()
    {
        lock (_gate)
        {
            return _byUser.Keys.Concat(_offlineDue.Keys).Distinct().ToList();
        }
    }

    public RealtimeConnection? Find(string connectionId)
    {
        lock (_gate)
        {
            return _connections.TryGetValue(connectionId, out var entry) ? new RealtimeConnection(connectionId, entry.UserId, entry.SessionId) : null;
        }
    }

    public IReadOnlyList<RealtimeConnection> Snapshot()
    {
        lock (_gate)
        {
            return _connections.Select(c => new RealtimeConnection(c.Key, c.Value.UserId, c.Value.SessionId)).ToList();
        }
    }

    /// <summary>Records that a connection joined a conversation. False when it had already joined (idempotent).</summary>
    public bool Join(string connectionId, Guid conversationId)
    {
        lock (_gate)
        {
            return _connections.TryGetValue(connectionId, out var entry) && entry.Conversations.Add(conversationId);
        }
    }

    /// <summary>Leaves a conversation. True when the user's typing indicator there ended as a result.</summary>
    public bool Leave(string connectionId, Guid conversationId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connectionId, out var entry) || !entry.Conversations.Remove(conversationId))
            {
                return false;
            }

            return !HasJoinedLocked(entry.UserId, conversationId) && _typing.Remove((conversationId, entry.UserId));
        }
    }

    public bool HasJoined(string connectionId, Guid conversationId)
    {
        lock (_gate)
        {
            return _connections.TryGetValue(connectionId, out var entry) && entry.Conversations.Contains(conversationId);
        }
    }

    /// <summary>
    /// Removes every connection of the user from the conversation (access revoked). Returns the
    /// affected connection ids and whether a typing indicator ended.
    /// </summary>
    public (IReadOnlyList<string> ConnectionIds, bool TypingStopped) RemoveUserFromConversation(Guid userId, Guid conversationId)
    {
        lock (_gate)
        {
            var removed = new List<string>();
            if (_byUser.TryGetValue(userId, out var ids))
            {
                foreach (var id in ids)
                {
                    if (_connections[id].Conversations.Remove(conversationId))
                    {
                        removed.Add(id);
                    }
                }
            }

            return (removed, _typing.Remove((conversationId, userId)));
        }
    }

    /// <summary>
    /// Starts, extends or stops the user's typing indicator. Returns whether the visible state
    /// changed (extensions do not) and the new expiry.
    /// </summary>
    public (bool Changed, DateTimeOffset? ExpiresAtUtc) SetTyping(Guid conversationId, Guid userId, bool isTyping)
    {
        lock (_gate)
        {
            var key = (conversationId, userId);
            if (!isTyping)
            {
                return (_typing.Remove(key), null);
            }

            var expires = time.GetUtcNow() + settings.Value.TypingTimeout;
            var started = !_typing.ContainsKey(key);
            _typing[key] = expires;
            return (started, expires);
        }
    }

    /// <summary>Applies due offline transitions and expires typing indicators.</summary>
    public SweepResult Sweep()
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            var offline = _offlineDue.Where(o => o.Value <= now).Select(o => o.Key).ToList();
            foreach (var userId in offline)
            {
                _offlineDue.Remove(userId);
            }

            var expired = _typing.Where(t => t.Value <= now).Select(t => t.Key).ToList();
            foreach (var key in expired)
            {
                _typing.Remove(key);
            }

            return new SweepResult(offline, expired);
        }
    }

    private bool IsOnlineLocked(Guid userId) => _byUser.ContainsKey(userId) || _offlineDue.ContainsKey(userId);

    private bool HasJoinedLocked(Guid userId, Guid conversationId) =>
        _byUser.TryGetValue(userId, out var ids) && ids.Any(id => _connections[id].Conversations.Contains(conversationId));

    private sealed record Entry(Guid UserId, Guid SessionId)
    {
        public HashSet<Guid> Conversations { get; } = [];
    }
}
