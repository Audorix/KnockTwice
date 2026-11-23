using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Domain.Sessions;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Sessions;

/// <summary>Persistence port for server-side sign-in sessions.</summary>
public interface ISessionStore
{
    Task<UserSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSession>> ListActiveAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    void Add(UserSession session);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes sessions that were revoked or expired before <paramref name="cutoff"/>, and the push
    /// subscriptions of those browsers (Phase 21). Returns how many sessions were deleted.
    /// </summary>
    Task<int> PurgeClosedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

/// <summary>
/// Session lifecycle: start on sign-in, validate on every authenticated request, list and revoke.
/// Activity is written at most every <see cref="TouchInterval"/> to avoid a write per request.
/// </summary>
public sealed class SessionService(ISessionStore store, TimeProvider time, IOptions<AccountSettings> settings)
{
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    public async Task<UserSession> StartAsync(Guid userId, string clientDescription, bool persistent, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var session = UserSession.Start(Guid.CreateVersion7(now), userId, clientDescription, persistent, now, Lifetime(persistent));
        store.Add(session);
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <summary>True when the session exists, belongs to the user, is not revoked and has not expired.</summary>
    public async Task<bool> ValidateAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken)
    {
        var session = await store.FindAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        if (session is null || session.UserId != userId || !session.IsActive(now))
        {
            return false;
        }

        if (now - session.LastSeenAtUtc >= TouchInterval)
        {
            session.Touch(now, Lifetime(session.Persistent));
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public Task<IReadOnlyList<UserSession>> ListActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        store.ListActiveAsync(userId, time.GetUtcNow(), cancellationToken);

    /// <summary>Revokes one of the caller's own sessions. Another user's session is reported as not found.</summary>
    public async Task RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await store.FindAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null || session.UserId != userId)
        {
            throw new ResourceNotFoundException("session_not_found");
        }

        session.Revoke(time.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Revokes every active session of the user except <paramref name="keepSessionId"/>. Returns the number revoked.</summary>
    public async Task<int> RevokeAllAsync(Guid userId, Guid? keepSessionId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var sessions = await store.ListActiveAsync(userId, now, cancellationToken).ConfigureAwait(false);
        var revoked = 0;
        foreach (var session in sessions.Where(s => s.Id != keepSessionId))
        {
            session.Revoke(now);
            revoked++;
        }

        if (revoked > 0)
        {
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return revoked;
    }

    public TimeSpan Lifetime(bool persistent) =>
        persistent ? settings.Value.PersistentSessionLifetime : settings.Value.SessionLifetime;
}
