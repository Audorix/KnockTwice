using Microsoft.EntityFrameworkCore;

namespace Audorix.ChatX.Infrastructure.Persistence;

/// <summary>
/// A database provider adapter. Each implementation lives in its own persistence project
/// together with its migrations and model snapshot.
/// </summary>
public interface IDatabaseProvider
{
    DatabaseProviderKind Kind { get; }

    /// <summary>
    /// Returns validation errors for a connection string. Messages must never echo the
    /// connection string or any part that may contain credentials.
    /// </summary>
    IReadOnlyList<string> ValidateConnectionString(string connectionString);

    /// <summary>Configures EF Core for this provider, including its migrations assembly.</summary>
    void Configure(DbContextOptionsBuilder builder, string connectionString);

    /// <summary>Builds a connection string from the structured values the setup wizard collects.</summary>
    string BuildConnectionString(DatabaseConnectionParameters parameters);

    /// <summary>
    /// Opens a short-lived connection to check the server, credentials and database. Never throws
    /// for connection problems and never reports provider messages, which can contain host details.
    /// </summary>
    Task<DatabaseProbeResult> ProbeAsync(string connectionString, CancellationToken cancellationToken);
}

/// <param name="Path">SQLite file path, absolute or relative to the host content root.</param>
/// <param name="SslMode">PostgreSQL SSL mode: <c>Disable</c>, <c>Prefer</c> or <c>Require</c>.</param>
public sealed record DatabaseConnectionParameters(
    string? Path = null,
    string? Host = null,
    int? Port = null,
    string? Database = null,
    string? Username = null,
    string? Password = null,
    string? SslMode = null);

public enum DatabaseProbeResult
{
    /// <summary>The database exists and accepted the connection.</summary>
    Connected,

    /// <summary>The server and credentials work, but the database does not exist yet (migrations create it).</summary>
    DatabaseMissing,

    AuthenticationFailed,

    Unreachable,

    Failed,
}
