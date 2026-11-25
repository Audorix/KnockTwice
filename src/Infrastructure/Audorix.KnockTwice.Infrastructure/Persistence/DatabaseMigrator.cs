using Audorix.KnockTwice.Application.Installations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Audorix.KnockTwice.Infrastructure.Persistence;

/// <summary>
/// Applies the selected provider's migrations and initializes installation metadata.
/// Invoked only by the explicit <c>migrate</c> command, never by normal API startup.
/// </summary>
public sealed class DatabaseMigrator(
    ChatXDbContext db,
    InstallationService installations,
    ILogger<DatabaseMigrator> logger)
{
    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken) =>
        (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();

    public async Task<DatabaseMigrationResult> MigrateAsync(CancellationToken cancellationToken)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        logger.LogInformation(
            "Applying {PendingCount} pending migration(s) using provider {Provider}.",
            pending.Count,
            db.Database.ProviderName);

        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        var initialization = await installations.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Installation metadata {InitializationState} ({InstallationId}).",
            initialization.Created ? "initialized" : "already present; no changes made",
            initialization.Installation.Id);

        return new DatabaseMigrationResult(pending, initialization.Created);
    }
}

public sealed record DatabaseMigrationResult(IReadOnlyList<string> AppliedMigrations, bool InstallationCreated);
