using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Infrastructure.Persistence;

/// <summary>
/// Startup validation for <see cref="DatabaseOptions"/>. There is deliberately no fallback
/// provider: a missing or unknown value fails startup with a message that names the
/// setting but never includes the connection string.
/// </summary>
internal sealed class DatabaseOptionsValidator(IEnumerable<IDatabaseProvider> providers) : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        var supported = string.Join(", ", DatabaseProviderKinds.Names);

        DatabaseProviderKind? kind = null;
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            errors.Add($"Database:Provider is required. Supported values: {supported}.");
        }
        else if (DatabaseProviderKinds.TryParse(options.Provider, out var parsed))
        {
            kind = parsed;
        }
        else
        {
            errors.Add($"Database:Provider '{Describe(options.Provider)}' is not supported. Supported values: {supported}.");
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            errors.Add("Database:ConnectionString is required (for example via the Database__ConnectionString environment variable).");
        }
        else if (kind is { } selected)
        {
            var provider = providers.FirstOrDefault(p => p.Kind == selected);
            if (provider is null)
            {
                errors.Add($"Database:Provider '{selected}' is not registered in this host.");
            }
            else
            {
                errors.AddRange(provider.ValidateConnectionString(options.ConnectionString));
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    // Provider names are not secrets, but bound the echoed value so a misplaced
    // connection string in the wrong setting is not reproduced in logs.
    private static string Describe(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 20 && !trimmed.Contains('=', StringComparison.Ordinal) && !trimmed.Contains(';', StringComparison.Ordinal)
            ? trimmed
            : "<redacted>";
    }
}
