using Audorix.ChatX.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Audorix.ChatX.Persistence.Sqlite;

/// <summary>
/// SQLite adapter for lightweight, single-instance installations and development.
/// A relative <c>Data Source</c> is resolved against <paramref name="baseDirectory"/>
/// (the host content root) so the database location does not depend on the shell's
/// working directory.
/// </summary>
public sealed class SqliteDatabaseProvider(string baseDirectory) : IDatabaseProvider
{
    public DatabaseProviderKind Kind => DatabaseProviderKind.Sqlite;

    public IReadOnlyList<string> ValidateConnectionString(string connectionString)
    {
        SqliteConnectionStringBuilder builder;
        try
        {
            builder = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            return ["Database:ConnectionString is not a valid SQLite connection string."];
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            return ["Database:ConnectionString must specify a SQLite 'Data Source'."];
        }

        if (builder.Mode is SqliteOpenMode.Memory || builder.DataSource.Trim() == ":memory:")
        {
            return ["Database:ConnectionString must reference a SQLite file; in-memory databases do not persist."];
        }

        return [];
    }

    public void Configure(DbContextOptionsBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSqlite(
            ResolveConnectionString(connectionString),
            sqlite => sqlite.MigrationsAssembly(typeof(SqliteDatabaseProvider).Assembly.GetName().Name));
    }

    public string BuildConnectionString(DatabaseConnectionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return new SqliteConnectionStringBuilder { DataSource = parameters.Path?.Trim() ?? string.Empty }.ToString();
    }

    public async Task<DatabaseProbeResult> ProbeAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = new SqliteConnectionStringBuilder(ResolveConnectionString(connectionString)) { Pooling = false };
            var existed = File.Exists(resolved.DataSource);
            await using (var connection = new SqliteConnection(resolved.ToString()))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT count(*) FROM sqlite_master";
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }

            if (existed)
            {
                return DatabaseProbeResult.Connected;
            }

            // The probe proved the location is writable; leave no empty file behind.
            File.Delete(resolved.DataSource);
            return DatabaseProbeResult.DatabaseMissing;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Not a database file, a locked file, or a directory that cannot be created.
            return DatabaseProbeResult.Failed;
        }
    }

    internal string ResolveConnectionString(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (!Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(baseDirectory, builder.DataSource));
        }

        var directory = Path.GetDirectoryName(builder.DataSource);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return builder.ToString();
    }
}
