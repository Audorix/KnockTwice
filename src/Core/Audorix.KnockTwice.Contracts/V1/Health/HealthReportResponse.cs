namespace Audorix.KnockTwice.Contracts.V1.Health;

/// <summary>
/// Body of <c>/health/live</c> and <c>/health/ready</c>. Values of <see cref="Status"/> and
/// <see cref="HealthCheckEntry.Status"/> are <c>healthy</c>, <c>degraded</c> or <c>unhealthy</c>.
/// No exception text, versions, hosts or connection details are included.
/// </summary>
public sealed record HealthReportResponse(string Status, IReadOnlyList<HealthCheckEntry> Checks);

/// <summary>A single named dependency check.</summary>
public sealed record HealthCheckEntry(string Name, string Status);
