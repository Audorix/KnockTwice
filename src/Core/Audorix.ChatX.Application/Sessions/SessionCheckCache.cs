using System.Collections.Concurrent;

namespace Audorix.ChatX.Application.Sessions;

/// <summary>
/// Recently validated sign-in sessions (Phase 23), so an active person's requests do not each read
/// their session row. An entry holds only who owns the session and when it expires, and lives
/// <see cref="Lifetime"/> at most. Revoking a session through <see cref="SessionService"/> removes it
/// here at once; with several API instances (scale-out) another instance may accept a revoked
/// session for up to <see cref="Lifetime"/>.
/// </summary>
public sealed class SessionCheckCache(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);

    /// <summary>Above this many entries the cache starts over (a memory bound, not a tuning knob).</summary>
    private const int MaxEntries = 100_000;

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    private sealed record Entry(Guid UserId, DateTimeOffset ExpiresAtUtc, DateTimeOffset LastSeenAtUtc, DateTimeOffset CachedAtUtc);

    /// <summary>
    /// True when the session was validated within <see cref="Lifetime"/>, belongs to the user, has not
    /// expired and does not need its activity written yet (<paramref name="touchInterval"/>).
    /// </summary>
    public bool IsKnownActive(Guid sessionId, Guid userId, TimeSpan touchInterval)
    {
        if (!_entries.TryGetValue(sessionId, out var entry))
        {
            return false;
        }

        var now = time.GetUtcNow();
        if (now - entry.CachedAtUtc >= Lifetime || now >= entry.ExpiresAtUtc || now - entry.LastSeenAtUtc >= touchInterval)
        {
            _entries.TryRemove(sessionId, out _);
            return false;
        }

        return entry.UserId == userId;
    }

    public void Remember(Guid sessionId, Guid userId, DateTimeOffset expiresAtUtc, DateTimeOffset lastSeenAtUtc)
    {
        if (_entries.Count >= MaxEntries)
        {
            _entries.Clear();
        }

        _entries[sessionId] = new Entry(userId, expiresAtUtc, lastSeenAtUtc, time.GetUtcNow());
    }

    public void Forget(Guid sessionId) => _entries.TryRemove(sessionId, out _);
}
