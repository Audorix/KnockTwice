namespace Audorix.ChatX.Contracts.V1.Auth;

/// <summary>Public sign-in configuration for the web app (no secrets).</summary>
/// <param name="AutoConfirmEmail">Development mode: registration creates a confirmed account and sends no email.</param>
public sealed record AuthConfigurationResponse(
    bool RegistrationEnabled,
    bool RequireConfirmedEmail,
    int PasswordMinimumLength,
    CaptchaConfigurationResponse Captcha,
    IReadOnlyList<string> ExternalProviders,
    bool AutoConfirmEmail,
    bool DemoDataEnabled,
    string RegistrationMode = "open");

/// <param name="Provider"><c>turnstile</c> or <c>none</c>.</param>
/// <param name="SiteKey">Public site key; null when no challenge is required.</param>
public sealed record CaptchaConfigurationResponse(string Provider, string? SiteKey);

/// <summary>Antiforgery token to send in <see cref="HeaderName"/> on every POST/PUT/PATCH/DELETE under <c>/api/v1/</c>.</summary>
public sealed record CsrfTokenResponse(string Token, string HeaderName);

public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName, string? Language, string? CaptchaToken);

public sealed record LoginRequest(string? Email, string? Password, bool RememberMe);

public sealed record EmailActionRequest(string? Email, string? CaptchaToken);

public sealed record ConfirmEmailRequest(Guid UserId, string? Token);

public sealed record ResetPasswordRequest(Guid UserId, string? Token, string? NewPassword);

/// <summary>Uniform response for operations that must not reveal whether an account exists.</summary>
public sealed record AcceptedResponse(string Status);

public sealed record SessionResponse(
    Guid Id,
    string Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool Persistent,
    bool Current);

public sealed record RevokedSessionsResponse(int Revoked);
