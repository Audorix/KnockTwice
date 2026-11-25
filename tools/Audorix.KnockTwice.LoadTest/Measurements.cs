using System.Collections.Concurrent;
using System.Diagnostics;

namespace Audorix.KnockTwice.LoadTest;

/// <summary>Latency samples for one kind of operation, and its failures.</summary>
internal sealed class Series(string name)
{
    private readonly ConcurrentBag<double> _samples = [];
    private readonly ConcurrentDictionary<string, int> _errors = new(StringComparer.Ordinal);

    public string Name { get; } = name;
    public bool Recording { get; set; }

    public void Add(long startedTimestamp)
    {
        if (Recording)
        {
            _samples.Add(Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds);
        }
    }

    public void Fail(string reason)
    {
        if (Recording)
        {
            _errors.AddOrUpdate(reason, 1, (_, n) => n + 1);
        }
    }

    public SeriesResult Result()
    {
        var sorted = _samples.ToArray();
        Array.Sort(sorted);
        double At(double p) => sorted.Length == 0 ? double.NaN : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new SeriesResult(Name, sorted.Length, _errors.Values.Sum(), At(0.50), At(0.95), At(0.99), sorted.Length == 0 ? double.NaN : sorted[^1],
            _errors.OrderByDescending(e => e.Value).Take(5).ToDictionary(e => e.Key, e => e.Value));
    }
}

internal sealed record SeriesResult(string Name, int Count, int Errors, double P50, double P95, double P99, double Max, IReadOnlyDictionary<string, int> ErrorReasons);

internal sealed record TargetResult(string Name, string Target, string Measured, bool Met);
