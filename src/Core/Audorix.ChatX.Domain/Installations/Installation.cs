using System.Text.RegularExpressions;

namespace Audorix.ChatX.Domain.Installations;

/// <summary>
/// Installation-level metadata (ADR-008 distinguishes installation-level data from
/// organization, workspace and user data). Exactly one record exists per installation.
/// It is created when the schema is first initialized and is internal: it is not exposed
/// through a public write endpoint.
/// </summary>
public sealed partial class Installation
{
    private Installation(Guid id, string defaultLanguage, DateTimeOffset initializedAtUtc)
    {
        Id = id;
        DefaultLanguage = defaultLanguage;
        InitializedAtUtc = initializedAtUtc;
        UpdatedAtUtc = initializedAtUtc;
    }

    /// <summary>Stable identifier of this installation.</summary>
    public Guid Id { get; private set; }

    /// <summary>Installation default UI language (ISO 639 language code, lower case).</summary>
    public string DefaultLanguage { get; private set; }

    public DateTimeOffset InitializedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Installation Initialize(Guid id, string defaultLanguage, DateTimeOffset nowUtc)
    {
        if (id == Guid.Empty)
        {
            throw new DomainRuleException("installation_id_required", "An installation identifier is required.");
        }

        return new Installation(id, NormalizeLanguage(defaultLanguage), EnsureUtc(nowUtc));
    }

    public void ChangeDefaultLanguage(string defaultLanguage, DateTimeOffset nowUtc)
    {
        var normalized = NormalizeLanguage(defaultLanguage);
        var utc = EnsureUtc(nowUtc);
        if (utc < UpdatedAtUtc)
        {
            throw new DomainRuleException("installation_clock_regression", "The update time precedes the last recorded update.");
        }

        DefaultLanguage = normalized;
        UpdatedAtUtc = utc;
    }

    private static string NormalizeLanguage(string language)
    {
        var normalized = language?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!LanguageCodePattern().IsMatch(normalized))
        {
            throw new DomainRuleException("invalid_language_code", "The language must be a two- or three-letter ISO 639 code.");
        }

        return normalized;
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException("timestamp_not_utc", "Timestamps must be expressed in UTC.");
        }

        return value;
    }

    [GeneratedRegex("^[a-z]{2,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageCodePattern();
}
