namespace Audorix.ChatX.Application.Common;

/// <summary>Base for expected, client-correctable failures carrying a stable error code.</summary>
public abstract class ApplicationRuleException : Exception
{
    protected ApplicationRuleException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Input failed validation. <see cref="Errors"/> maps field names to error codes.</summary>
public sealed class RequestValidationException : ApplicationRuleException
{
    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("validation_failed", "One or more values are not valid.")
    {
        Errors = errors;
    }

    public RequestValidationException(string field, params string[] codes)
        : this(new Dictionary<string, string[]> { [field] = codes })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>The captcha challenge failed (<c>captcha_failed</c>) or could not be checked (<c>captcha_unavailable</c>).</summary>
public sealed class CaptchaRejectedException(string code) : ApplicationRuleException(code, "Human verification did not succeed.")
{
    public bool ProviderUnavailable => Code == "captcha_unavailable";
}

/// <summary>A feature is disabled by configuration.</summary>
public sealed class FeatureDisabledException(string code) : ApplicationRuleException(code, "This feature is disabled.");

/// <summary>A resource does not exist or is not visible to the caller.</summary>
public sealed class ResourceNotFoundException(string code) : ApplicationRuleException(code, "The resource was not found.");

/// <summary>The caller can see the resource but lacks the permission for this operation (403).</summary>
public sealed class ForbiddenException(string code = "forbidden") : ApplicationRuleException(code, "The operation is not permitted.");

/// <summary>The operation conflicts with the current state (409), for example removing the last owner.</summary>
public sealed class ConflictException(string code) : ApplicationRuleException(code, "The operation conflicts with the current state.");

/// <summary>The request body exceeds a configured limit (413), for example a file size or storage quota.</summary>
public sealed class PayloadTooLargeException(string code) : ApplicationRuleException(code, "The content exceeds a configured limit.");

/// <summary>A dependency the operation needs is unavailable right now (503).</summary>
public sealed class DependencyUnavailableException(string code) : ApplicationRuleException(code, "A required service is unavailable.");

/// <summary>A one-time token is invalid, expired or already used.</summary>
public sealed class InvalidTokenException() : ApplicationRuleException("invalid_or_expired_token", "The link is invalid or has expired.");
