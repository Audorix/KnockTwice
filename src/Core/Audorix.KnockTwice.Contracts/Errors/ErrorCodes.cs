namespace Audorix.KnockTwice.Contracts.Errors;

/// <summary>
/// Stable machine-readable error codes carried in the <c>code</c> member of RFC 9457
/// problem responses. Codes are part of the public contract: add new ones, never rename.
/// Validation failures (<see cref="ValidationFailed"/>) also carry an <c>errors</c> object
/// mapping request fields to arrays of field-level codes (for example <c>password_too_short</c>).
/// </summary>
public static class ErrorCodes
{
    public const string BadRequest = "bad_request";
    public const string ValidationFailed = "validation_failed";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string UnsupportedMediaType = "unsupported_media_type";
    public const string Conflict = "conflict";
    public const string TooManyRequests = "too_many_requests";
    public const string DomainRuleViolation = "domain_rule_violation";
    public const string ServiceUnavailable = "service_unavailable";
    public const string InternalError = "internal_error";

    // Identity and authentication (Phase 2)
    public const string CsrfTokenInvalid = "csrf_token_invalid";
    public const string InvalidCredentials = "invalid_credentials";
    public const string EmailNotConfirmed = "email_not_confirmed";
    public const string InvalidOrExpiredToken = "invalid_or_expired_token";
    public const string RegistrationDisabled = "registration_disabled";
    public const string CaptchaFailed = "captcha_failed";
    public const string CaptchaUnavailable = "captcha_unavailable";
    public const string EmailUnavailable = "email_unavailable";
    public const string ExternalProviderNotEnabled = "external_provider_not_enabled";
    public const string SessionNotFound = "session_not_found";

    // Setup wizard
    public const string SetupRequired = "setup_required";
    public const string SetupTokenInvalid = "setup_token_invalid";
    public const string SetupAlreadyCompleted = "setup_already_completed";
    public const string SetupAdministratorExists = "setup_administrator_exists";
    public const string SetupSettingsInvalid = "setup_settings_invalid";
    public const string SetupDatabaseUnavailable = "setup_database_unavailable";
    public const string SetupInProgress = "setup_in_progress";
}
