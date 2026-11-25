using System.Collections.Concurrent;

namespace Audorix.KnockTwice.Application.Common;

/// <summary>
/// Counts events per key against a limit within a time window (Phase 21, ADR-035). The default is
/// per API instance (<see cref="LocalRateCounter"/>); with scale-out configured the counts are kept
/// in Redis and shared by every instance.
/// </summary>
public interface IRateCounter
{
    /// <summary>Counts one event for <paramref name="key"/>; false when the limit within <paramref name="window"/> was already reached.</summary>
    bool TryAcquire(string key, int limit, TimeSpan window);
}

/// <summary>Process-local sliding-window counters (one API instance).</summary>
public sealed class LocalRateCounter(TimeProvider time) : IRateCounter
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _windows = new(StringComparer.Ordinal);

    public bool TryAcquire(string key, int limit, TimeSpan window)
    {
        var now = time.GetUtcNow();
        var queue = _windows.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() >= window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= limit)
            {
                return false;
            }

            queue.Enqueue(now);
            if (_windows.Count > 100_000)
            {
                _windows.Clear(); // crude bound on memory under a flood; limits restart.
            }

            return true;
        }
    }
}
