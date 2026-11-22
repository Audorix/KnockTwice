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
}
