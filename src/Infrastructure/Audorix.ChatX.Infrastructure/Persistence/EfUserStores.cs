using Audorix.ChatX.Application.Sessions;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Domain.Sessions;
using Audorix.ChatX.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Audorix.ChatX.Infrastructure.Persistence;

internal sealed class EfUserProfileStore(ChatXDbContext db) : IUserProfileStore
{
    public Task<UserProfile?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.UserProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

internal sealed class EfSessionStore(ChatXDbContext db) : ISessionStore
{
    public Task<UserSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken) =>
        db.UserSessions.SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

    public async Task<IReadOnlyList<UserSession>> ListActiveAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        // Expiry is filtered in memory: SQLite cannot compare DateTimeOffset values server-side,
        // and a user has few unrevoked sessions.
        var unrevoked = await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return unrevoked.Where(s => s.IsActive(nowUtc)).OrderByDescending(s => s.LastSeenAtUtc).ToList();
    }

    public void Add(UserSession session) => db.UserSessions.Add(session);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<int> PurgeClosedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        // Dates are compared in memory (SQLite cannot compare DateTimeOffset values); only the three
        // columns needed are read, and deletion runs in chunks.
        var rows = await db.UserSessions.AsNoTracking()
            .Select(s => new { s.Id, s.ExpiresAtUtc, s.RevokedAtUtc })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var closed = rows.Where(r => (r.RevokedAtUtc ?? r.ExpiresAtUtc) < cutoff).Select(r => r.Id).ToList();
        var deleted = 0;
        foreach (var chunk in closed.Chunk(500))
        {
            await db.PushSubscriptions.Where(p => chunk.Contains(p.SessionId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            deleted += await db.UserSessions.Where(s => chunk.Contains(s.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }
}
