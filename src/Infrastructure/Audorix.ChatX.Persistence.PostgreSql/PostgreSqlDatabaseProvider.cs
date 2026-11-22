using Audorix.ChatX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Audorix.ChatX.Persistence.PostgreSql;

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
}
