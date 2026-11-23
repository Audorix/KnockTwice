namespace Audorix.ChatX.Application.Abstractions;

/// <summary>
/// Human-verification port. Implementations live in Infrastructure and are selected by
/// <c>Captcha:Provider</c> (Cloudflare Turnstile by default).
/// </summary>
public interface ICaptchaVerifier
{
    /// <summary>Public, non-secret settings the browser needs to render the challenge.</summary>
    CaptchaClientConfiguration ClientConfiguration { get; }

    /// <summary>
    /// Verifies a token produced by the browser widget. Returns <see cref="CaptchaOutcome.Unavailable"/>
    /// when the provider cannot be reached, so callers fail closed.
    /// </summary>
    Task<CaptchaOutcome> VerifyAsync(string? token, string action, string? remoteIp, CancellationToken cancellationToken);
}

/// <param name="Provider">Provider name in lower case (<c>turnstile</c> or <c>none</c>).</param>
/// <param name="SiteKey">Public site key, or <see langword="null"/> when no challenge is shown.</param>
public sealed record CaptchaClientConfiguration(string Provider, string? SiteKey);

public enum CaptchaOutcome
{
    Passed = 1,
    Failed = 2,
    Unavailable = 3,
}

/// <summary>Captcha actions; providers that support actions bind the token to one of these.</summary>
public static class CaptchaActions
{
    public const string Register = "register";
    public const string ForgotPassword = "forgot_password";
    public const string ResendConfirmation = "resend_confirmation";
}
