using Audorix.KnockTwice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Audorix.KnockTwice.Persistence.PostgreSql;

/// <summary>PostgreSQL adapter: the production-oriented database profile.</summary>
public sealed class PostgreSqlDatabaseProvider : IDatabaseProvider
{
    public DatabaseProviderKind Kind => DatabaseProviderKind.PostgreSql;

    public IReadOnlyList<string> ValidateConnectionString(string connectionString)
    {
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            // The parser's message can quote the offending fragment; do not surface it.
            return ["Database:ConnectionString is not a valid PostgreSQL connection string."];
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(builder.Host))
        {
            errors.Add("Database:ConnectionString must specify a PostgreSQL 'Host'.");
        }

        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            errors.Add("Database:ConnectionString must specify a PostgreSQL 'Database'.");
        }

        return errors;
    }

    public void Configure(DbContextOptionsBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(PostgreSqlDatabaseProvider).Assembly.GetName().Name));
    }

    public string BuildConnectionString(DatabaseConnectionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = parameters.Host?.Trim(),
            Port = parameters.Port ?? NpgsqlConnection.DefaultPort,
            Database = parameters.Database?.Trim(),
            Username = parameters.Username?.Trim(),
            SslMode = Enum.TryParse<SslMode>(parameters.SslMode, ignoreCase: true, out var ssl) ? ssl : SslMode.Prefer,
        };
        if (!string.IsNullOrEmpty(parameters.Password))
        {
            builder.Password = parameters.Password;
        }

        return builder.ConnectionString;
    }

    public async Task<DatabaseProbeResult> ProbeAsync(string connectionString, CancellationToken cancellationToken)
    {
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Timeout = 5 };
        }
        catch (ArgumentException)
        {
            return DatabaseProbeResult.Failed;
        }

        var target = await TryOpenAsync(builder.ConnectionString, cancellationToken).ConfigureAwait(false);
        if (target != DatabaseProbeResult.DatabaseMissing)
        {
            return target;
        }

        // The server answered "database does not exist": confirm the credentials against the
        // maintenance database, because migrations will create the target database.
        builder.Database = "postgres";
        var maintenance = await TryOpenAsync(builder.ConnectionString, cancellationToken).ConfigureAwait(false);
        return maintenance == DatabaseProbeResult.Connected ? DatabaseProbeResult.DatabaseMissing : maintenance;
    }

    private static async Task<DatabaseProbeResult> TryOpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return DatabaseProbeResult.Connected;
        }
        catch (PostgresException ex)
        {
            return ex.SqlState switch
            {
                PostgresErrorCodes.InvalidCatalogName => DatabaseProbeResult.DatabaseMissing,
                PostgresErrorCodes.InvalidPassword or PostgresErrorCodes.InvalidAuthorizationSpecification => DatabaseProbeResult.AuthenticationFailed,
                _ => DatabaseProbeResult.Failed,
            };
        }
        catch (NpgsqlException)
        {
            return DatabaseProbeResult.Unreachable;
        }
        catch (Exception ex) when (ex is TimeoutException or System.Net.Sockets.SocketException or ArgumentException)
        {
            return DatabaseProbeResult.Unreachable;
        }
    }
}
