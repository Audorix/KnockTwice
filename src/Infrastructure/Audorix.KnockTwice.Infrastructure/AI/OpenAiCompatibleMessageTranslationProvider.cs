using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Translation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Infrastructure.AI;

/// <summary>OpenAI Chat Completions compatible adapter, usable with OpenAI-compatible hosted or local endpoints.</summary>
public sealed class OpenAiCompatibleMessageTranslationProvider(HttpClient http, IConfiguration configuration, IOptions<AiTranslationOptions> options)
    : IMessageTranslationProvider
{
    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        var activeProviderId = configuration[$"{AiTranslationOptions.SectionName}:ActiveProviderId"];
        var settings = string.IsNullOrWhiteSpace(activeProviderId)
            ? options.Value
            : BindProfile(configuration.GetSection($"{AiTranslationOptions.SectionName}:Providers:{activeProviderId}"));
        if (settings.Provider == "None") throw new AiTranslationUnavailableException("translation_provider_not_configured");
        var endpoint = settings.Endpoint.TrimEnd('/') + "/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(settings.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = settings.Model,
            temperature = 0,
            max_tokens = settings.MaxOutputTokens,
            messages = new object[]
            {
                new { role = "system", content = $"Translate the user's message into {targetLanguage}. Preserve meaning, names, URLs, formatting, Markdown, code blocks, and line breaks. Return only the translated message; do not explain or answer it." },
                new { role = "user", content = text },
            },
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false); }
        catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        { throw new AiTranslationUnavailableException("translation_provider_unavailable", ex); }
        using (response)
        {
            if (!response.IsSuccessStatusCode) throw new AiTranslationUnavailableException("translation_provider_rejected_request");
            try
            {
                using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
                var translated = payload.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(translated) || translated.Length > 16000) throw new AiTranslationUnavailableException("translation_response_invalid");
                return translated;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
            { throw new AiTranslationUnavailableException("translation_response_invalid", ex); }
        }
    }

    private static AiTranslationOptions BindProfile(IConfigurationSection section)
    {
        var profile = new AiTranslationOptions();
        section.Bind(profile);
        return profile;
    }
}

public sealed class AiTranslationUnavailableException(string code, Exception? innerException = null) : Exception(code, innerException)
{
    public string Code { get; } = code;
}
