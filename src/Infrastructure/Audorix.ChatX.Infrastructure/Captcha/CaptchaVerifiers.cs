using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Audorix.ChatX.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Infrastructure.Captcha;

/// <summary>
/// Cloudflare Turnstile server-side validation (siteverify). Fails closed: a network error or
/// timeout yields <see cref="CaptchaOutcome.Unavailable"/>, never a pass.
/// See https://developers.cloudflare.com/turnstile/get-started/server-side-validation/.
/// </summary>
internal sealed class TurnstileCaptchaVerifier(HttpClient http, IOptions<CaptchaOptions> options, ILogger<TurnstileCaptchaVerifier> logger)
    : ICaptchaVerifier
{
    public const string HttpClientName = "turnstile";

    /// <summary>Turnstile tokens are at most 2048 characters.</summary>
    private const int MaxTokenLength = 2048;

    public CaptchaClientConfiguration ClientConfiguration => new("turnstile", options.Value.Turnstile.SiteKey);

    public async Task<CaptchaOutcome> VerifyAsync(string? token, string action, string? remoteIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            return CaptchaOutcome.Failed;
        }

        var settings = options.Value.Turnstile;
        var form = new Dictionary<string, string>
        {
            ["secret"] = settings.SecretKey!,
            ["response"] = token,
            ["idempotency_key"] = Guid.NewGuid().ToString(),
        };
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            form["remoteip"] = remoteIp;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await http.PostAsync(settings.VerifyUrl, content, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Turnstile verification returned HTTP {StatusCode}.", (int)response.StatusCode);
                return CaptchaOutcome.Unavailable;
            }

            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(timeout.Token).ConfigureAwait(false);
            if (result is null)
            {
                return CaptchaOutcome.Unavailable;
            }

            if (!result.Success)
            {
                logger.LogInformation("Turnstile rejected a token: {ErrorCodes}.", string.Join(",", result.ErrorCodes ?? []));
                return CaptchaOutcome.Failed;
            }

            // Bind the token to the operation it was issued for when the widget declared an action.
            if (!string.IsNullOrEmpty(result.Action) && !string.Equals(result.Action, action, StringComparison.Ordinal))
            {
                logger.LogInformation("Turnstile token action {Actual} did not match {Expected}.", result.Action, action);
                return CaptchaOutcome.Failed;
            }

            if (settings.AllowedHostnames.Count > 0 && !settings.AllowedHostnames.Contains(result.Hostname ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogInformation("Turnstile token was solved on an unexpected hostname.");
                return CaptchaOutcome.Failed;
            }

            return CaptchaOutcome.Passed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Turnstile verification timed out.");
            return CaptchaOutcome.Unavailable;
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Turnstile verification endpoint is unreachable.");
            return CaptchaOutcome.Unavailable;
        }
        catch (System.Text.Json.JsonException)
        {
            logger.LogWarning("Turnstile verification returned an unreadable response.");
            return CaptchaOutcome.Unavailable;
        }
    }

    internal sealed record SiteVerifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes,
        [property: JsonPropertyName("hostname")] string? Hostname,
        [property: JsonPropertyName("action")] string? Action);
}

/// <summary><c>Captcha:Provider = None</c>: every request passes. Intended for offline development and tests only.</summary>
internal sealed class DisabledCaptchaVerifier : ICaptchaVerifier
{
    public CaptchaClientConfiguration ClientConfiguration { get; } = new("none", null);

    public Task<CaptchaOutcome> VerifyAsync(string? token, string action, string? remoteIp, CancellationToken cancellationToken) =>
        Task.FromResult(CaptchaOutcome.Passed);
}
