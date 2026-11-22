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
