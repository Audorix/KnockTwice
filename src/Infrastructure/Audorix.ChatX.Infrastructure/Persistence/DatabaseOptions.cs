namespace Audorix.ChatX.Infrastructure.Persistence;

/// <summary>
/// Database settings bound from the <c>Database</c> configuration section.
/// Supply <see cref="ConnectionString"/> through environment variables or a secret store
/// (for example <c>Database__ConnectionString</c>); never commit real credentials.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary><c>Sqlite</c> or <c>PostgreSql</c> (case-insensitive). There is no default.</summary>
    public string? Provider { get; set; }

    public string? ConnectionString { get; set; }

    /// <summary>The parsed provider. Only valid after options validation succeeded.</summary>
    public DatabaseProviderKind ProviderKind =>
        DatabaseProviderKinds.TryParse(Provider, out var kind)
            ? kind
            : throw new InvalidOperationException("Database options have not been validated.");
}

public enum DatabaseProviderKind
{
    Sqlite = 1,
    PostgreSql = 2,
}

public static class DatabaseProviderKinds
{
    public static IReadOnlyList<string> Names { get; } = ["Sqlite", "PostgreSql"];

    public static bool TryParse(string? value, out DatabaseProviderKind kind)
    {
        // Only the two explicit names are accepted; numeric enum values and aliases are rejected.
        switch (value?.Trim().ToUpperInvariant())
        {
            case "SQLITE":
                kind = DatabaseProviderKind.Sqlite;
                return true;
            case "POSTGRESQL":
                kind = DatabaseProviderKind.PostgreSql;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
