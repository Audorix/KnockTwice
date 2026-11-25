namespace Audorix.KnockTwice.Application.Realtime;

/// <summary>
/// Presence shared between API instances (Phase 21, ADR-035). Each instance keeps its own
/// connections in <see cref="RealtimeState"/>; this records which instances currently have each
/// user online, so "online" means online on any instance. The single-server default records
/// nothing (<see cref="LocalClusterPresence"/>); the Redis implementation is used when scale-out is
/// configured.
/// </summary>
public interface IClusterPresence
{
    /// <summary>This instance now has the user online.</summary>
    Task MarkOnlineAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>This instance no longer has the user online (after the offline grace period).</summary>
    Task MarkOfflineAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Of <paramref name="userIds"/>, those online on another instance.</summary>
    Task<IReadOnlySet<Guid>> OnlineElsewhereAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Heartbeat: renews this instance's entries for its online users (entries of a crashed instance expire).</summary>
    Task RefreshAsync(IReadOnlyCollection<Guid> onlineHere, CancellationToken cancellationToken);
}

/// <summary>One API instance: nobody is online "elsewhere".</summary>
public sealed class LocalClusterPresence : IClusterPresence
{
    private static readonly IReadOnlySet<Guid> None = new HashSet<Guid>();

    public Task MarkOnlineAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task MarkOfflineAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlySet<Guid>> OnlineElsewhereAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) => Task.FromResult(None);

    public Task RefreshAsync(IReadOnlyCollection<Guid> onlineHere, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// "Is this person online?" for notifications and mentions: online on this instance (including the
/// offline grace period) or on any other instance.
/// </summary>
public sealed class PresenceDirectory(RealtimeState state, IClusterPresence cluster)
{
    public async Task<bool> IsOnlineAsync(Guid userId, CancellationToken cancellationToken) =>
        state.IsOnline(userId) || (await cluster.OnlineElsewhereAsync([userId], cancellationToken).ConfigureAwait(false)).Contains(userId);

    public async Task<IReadOnlySet<Guid>> OnlineAmongAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var online = state.OnlineAmong(userIds).ToHashSet();
        var rest = userIds.Where(id => !online.Contains(id)).Distinct().ToList();
        if (rest.Count > 0)
        {
            online.UnionWith(await cluster.OnlineElsewhereAsync(rest, cancellationToken).ConfigureAwait(false));
        }

        return online;
    }
}
