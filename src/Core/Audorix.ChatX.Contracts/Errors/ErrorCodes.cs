namespace Audorix.ChatX.Contracts.Errors;

/// <summary>
/// Stable machine-readable error codes carried in the <c>code</c> member of RFC 9457
/// problem responses. Codes are part of the public contract: add new ones, never rename.
/// </summary>
public static class ErrorCodes
{
    public const string BadRequest = "bad_request";
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string UnsupportedMediaType = "unsupported_media_type";
    public const string Conflict = "conflict";
    public const string DomainRuleViolation = "domain_rule_violation";
    public const string ServiceUnavailable = "service_unavailable";
    public const string InternalError = "internal_error";
}
