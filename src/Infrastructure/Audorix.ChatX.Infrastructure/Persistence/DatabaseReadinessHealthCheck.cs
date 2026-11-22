using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Audorix.ChatX.Infrastructure.Persistence;

/// <summary>
/// Readiness requires a reachable database, no pending migrations for the selected
/// provider and an initialized installation record. Failure reasons are logged with a
/// stable code; the public health response only carries the status.
/// </summary>
internal sealed class DatabaseReadinessHealthCheck(
    ChatXDbContext db,
    ILogger<DatabaseReadinessHealthCheck> logger) : IHealthCheck
{
    public const string Name = "database";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return NotReady("database_unavailable");
            }

            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
            if (pending.Any())
            {
                return NotReady("schema_migrations_pending");
            }

            if (!await db.Installations.AsNoTracking().AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                return NotReady("installation_not_initialized");
            }

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Any provider failure means "not ready"; details stay in server logs.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Log the exception type only: provider messages can include host names.
            logger.LogWarning("Database readiness check failed with {ExceptionType}.", ex.GetType().Name);
            return NotReady("database_error");
        }
    }

    private HealthCheckResult NotReady(string reason)
    {
        logger.LogWarning("Database is not ready: {Reason}.", reason);
        return HealthCheckResult.Unhealthy(reason);
    }
}
