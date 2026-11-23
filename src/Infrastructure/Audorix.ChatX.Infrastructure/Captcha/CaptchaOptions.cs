using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Infrastructure.Captcha;

/// <summary>
/// Human-verification settings bound from the <c>Captcha</c> section. <see cref="Provider"/> is
/// <c>Turnstile</c> (Cloudflare, default) or <c>None</c> (explicitly disables challenges; logged as
/// a warning at startup). The Turnstile secret key comes from environment variables or a secret store.
/// </summary>
public sealed class CaptchaOptions
{
    public const string SectionName = "Captcha";

    public string Provider { get; set; } = CaptchaProviders.Turnstile;

    public TurnstileOptions Turnstile { get; set; } = new();
}

public sealed class TurnstileOptions
{
    /// <summary>Public site key rendered by the browser widget.</summary>
    public string? SiteKey { get; set; }

    /// <summary>Secret key used for server-side verification.</summary>
    public string? SecretKey { get; set; }

    public Uri VerifyUrl { get; set; } = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    /// <summary>
    /// Optional hostnames the token must have been solved on. Empty accepts any hostname
    /// configured for the site key in the Cloudflare dashboard.
    /// </summary>
    public IList<string> AllowedHostnames { get; } = [];

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

public static class CaptchaProviders
{
    public const string Turnstile = "Turnstile";
    public const string None = "None";

    public static IReadOnlyList<string> Names { get; } = [Turnstile, None];

    public static string? Normalize(string? value) =>
        Names.FirstOrDefault(n => string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

internal sealed class CaptchaOptionsValidator : IValidateOptions<CaptchaOptions>
{
    public ValidateOptionsResult Validate(string? name, CaptchaOptions options)
    {
        var provider = CaptchaProviders.Normalize(options.Provider);
        if (provider is null)
        {
            return ValidateOptionsResult.Fail($"Captcha:Provider is not supported. Supported values: {string.Join(", ", CaptchaProviders.Names)}.");
        }

        if (provider != CaptchaProviders.Turnstile)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Turnstile.SiteKey))
        {
            errors.Add("Captcha:Turnstile:SiteKey is required when Captcha:Provider is Turnstile.");
        }

        if (string.IsNullOrWhiteSpace(options.Turnstile.SecretKey))
        {
            errors.Add("Captcha:Turnstile:SecretKey is required when Captcha:Provider is Turnstile (set Captcha__Turnstile__SecretKey).");
        }

        if (options.Turnstile.VerifyUrl is not { IsAbsoluteUri: true, Scheme: "https" })
        {
            errors.Add("Captcha:Turnstile:VerifyUrl must be an absolute https URL.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
