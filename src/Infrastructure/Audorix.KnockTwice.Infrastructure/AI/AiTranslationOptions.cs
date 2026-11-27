using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Infrastructure.AI;

public sealed class AiTranslationOptions
{
    public const string SectionName = "AI:Translation";
    public string Provider { get; set; } = "None";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int MaxOutputTokens { get; set; } = 4000;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(45);
}

public sealed class AiTranslationOptionsValidator : IValidateOptions<AiTranslationOptions>
{
    public ValidateOptionsResult Validate(string? name, AiTranslationOptions options)
    {
        var errors = new List<string>();
        var provider = options.Provider?.Trim() ?? string.Empty;
        if (provider is not ("None" or "OpenAICompatible" or "Ollama"))
            errors.Add("AI:Translation:Provider must be None, OpenAICompatible, or Ollama.");
        if (provider != "None")
        {
            if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
                errors.Add("AI:Translation:Endpoint must be an absolute HTTP or HTTPS base URL.");
            if (string.IsNullOrWhiteSpace(options.Model)) errors.Add("AI:Translation:Model is required when translation is enabled.");
            if (provider == "OpenAICompatible" && string.IsNullOrWhiteSpace(options.ApiKey))
                errors.Add("AI:Translation:ApiKey is required for OpenAI-compatible providers.");
        }
        if (options.MaxOutputTokens is < 64 or > 16000) errors.Add("AI:Translation:MaxOutputTokens must be between 64 and 16000.");
        if (options.Timeout < TimeSpan.FromSeconds(5) || options.Timeout > TimeSpan.FromMinutes(2)) errors.Add("AI:Translation:Timeout must be between 5 seconds and 2 minutes.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
