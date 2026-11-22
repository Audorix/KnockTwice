using System.Text.Json;
using Audorix.ChatX.Contracts.V1.Health;
using Audorix.ChatX.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Audorix.ChatX.Api.Health;

/// <summary>
/// <c>/health/live</c>: the process is serving requests; it runs no dependency checks, so a
/// database outage does not cause restarts. <c>/health/ready</c>: the selected database is
/// reachable and its schema is current; returns 503 otherwise. Neither endpoint requires
/// authentication, and neither reports versions, hosts or exception details.
/// </summary>
internal static class HealthEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapChatXHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = static _ => false,
            ResponseWriter = WriteAsync,
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains(DependencyInjection.ReadinessTag),
            ResponseWriter = WriteAsync,
        });

        return endpoints;
    }

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        var body = new HealthReportResponse(
            Format(report.Status),
            report.Entries.Select(e => new HealthCheckEntry(e.Key, Format(e.Value.Status))).ToList());
        return JsonSerializer.SerializeAsync(context.Response.Body, body, JsonOptions, context.RequestAborted);
    }

    private static string Format(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "healthy",
        HealthStatus.Degraded => "degraded",
        _ => "unhealthy",
    };
}
