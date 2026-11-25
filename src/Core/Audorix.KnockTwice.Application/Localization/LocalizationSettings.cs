namespace Audorix.KnockTwice.Application.Localization;

/// <summary>
/// UI localization settings bound from the <c>Localization</c> configuration section.
/// </summary>
public sealed class LocalizationSettings
{
    public const string SectionName = "Localization";

    /// <summary>Languages for which this build ships resources (frontend and backend).</summary>
    public static IReadOnlyList<string> AvailableLanguages { get; } = ["en", "es"];

    public string DefaultLanguage { get; set; } = "en";

    public IList<string> SupportedLanguages { get; } = [];

    /// <summary>Supported languages, or every available language when none is configured.</summary>
    public IReadOnlyList<string> EffectiveSupportedLanguages =>
        SupportedLanguages.Count == 0
            ? AvailableLanguages
            : SupportedLanguages.Select(l => l.Trim().ToLowerInvariant()).Distinct().ToList();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        foreach (var language in EffectiveSupportedLanguages)
        {
            if (!AvailableLanguages.Contains(language))
            {
                errors.Add(
                    $"Localization:SupportedLanguages contains '{Truncate(language)}', which has no resources. " +
                    $"Available languages: {string.Join(", ", AvailableLanguages)}.");
            }
        }

        var defaultLanguage = DefaultLanguage?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(defaultLanguage))
        {
            errors.Add("Localization:DefaultLanguage is required.");
        }
        else if (!EffectiveSupportedLanguages.Contains(defaultLanguage))
        {
            errors.Add("Localization:DefaultLanguage must be one of Localization:SupportedLanguages.");
        }

        return errors;
    }

    private static string Truncate(string value) => value.Length <= 16 ? value : value[..16] + "…";
}
