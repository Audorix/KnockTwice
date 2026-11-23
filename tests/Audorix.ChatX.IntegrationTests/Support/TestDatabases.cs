using Microsoft.Data.Sqlite;
using Npgsql;

namespace Audorix.ChatX.IntegrationTests.Support;

/// <summary>
/// Disposable, real databases. SQLite uses a fresh file in a temporary directory.
/// PostgreSQL creates a uniquely named database on the server given by the
/// <c>CHATX_TEST_POSTGRES</c> connection string (an account allowed to CREATE DATABASE)
/// and drops it afterwards; it never touches any other database on that server.
/// </summary>
public abstract class TestDatabase : IAsyncDisposable
{
    public const string PostgresEnvironmentVariable = "CHATX_TEST_POSTGRES";

    public abstract string Provider { get; }

    public abstract string ConnectionString { get; }

    public static async Task<TestDatabase> CreateAsync(string provider) => provider switch
    {
        "Sqlite" => new SqliteTestDatabase(),
        "PostgreSql" => await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    public abstract ValueTask DisposeAsync();
}

public sealed class SqliteTestDatabase : TestDatabase
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "chatx-tests", Guid.NewGuid().ToString("N"));

    public SqliteTestDatabase()
    {
        Directory.CreateDirectory(_directory);
        FilePath = Path.Combine(_directory, "chatx.db");
    }

    public string FilePath { get; }

    public override string Provider => "Sqlite";

    // Pooled like production. Without pooling every operation opens and closes the WAL-mode
    // file, and with concurrent hub polling and background sweeps SQLite can then answer
    // "database is locked" immediately. The pool is cleared on dispose to release the file.
    public override string ConnectionString => $"Data Source={FilePath}";

    public override ValueTask DisposeAsync()
    {
        // The provider rewrites the connection string (absolute path), so clear every idle pooled
        // connection rather than one pool key; connections in use elsewhere are unaffected.
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: temp files are harmless if a handle is still closing.
        }

        return ValueTask.CompletedTask;
    }
}

public sealed class PostgreSqlTestDatabase : TestDatabase
{
    private readonly string _adminConnectionString;
    private readonly string _databaseName;

    private PostgreSqlTestDatabase(string adminConnectionString, string databaseName, string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        _databaseName = databaseName;
        ConnectionString = connectionString;
    }

    public override string Provider => "PostgreSql";

    public override string ConnectionString { get; }

    public static string? AdminConnectionString => Environment.GetEnvironmentVariable(PostgresEnvironmentVariable);

    public static async Task<PostgreSqlTestDatabase> CreateAsync()
    {
        var admin = AdminConnectionString;
        if (string.IsNullOrWhiteSpace(admin))
        {
            Assert.Skip($"PostgreSQL integration tests need {PostgresEnvironmentVariable} (see docs/development/testing.md).");
        }

        var name = "chatx_test_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var builder = new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = false };
        return new PostgreSqlTestDatabase(admin!, name, builder.ConnectionString);
    }

    public override async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
