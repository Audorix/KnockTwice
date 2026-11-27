using System.Text.RegularExpressions;
using Audorix.KnockTwice.Application.Common;

namespace Audorix.KnockTwice.Application.Translation;

public interface IMessageTranslationProvider
{
    Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken);
}

/// <summary>Validates request bounds before forwarding a user-requested translation to the configured provider.</summary>
public sealed class MessageTranslationService(IMessageTranslationProvider provider)
{
    private static readonly Regex LanguagePattern = new("^[a-zA-Z]{2,3}(?:-[a-zA-Z0-9]{2,8})?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Task<string> TranslateAsync(string? text, string? targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new RequestValidationException("message", "translation_text_required");
        if (text.Length > 4000) throw new RequestValidationException("message", "translation_text_too_long");
        var language = targetLanguage?.Trim();
        if (language is null || !LanguagePattern.IsMatch(language)) throw new RequestValidationException("targetLanguage", "language_invalid");
        return provider.TranslateAsync(text, language, cancellationToken);
    }
}
