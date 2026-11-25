using Microsoft.AspNetCore.Antiforgery;

namespace Audorix.KnockTwice.Api.Identity;

/// <summary>Validates the antiforgery token on state-changing requests (POST, PUT, PATCH, DELETE).</summary>
internal sealed class AntiforgeryEndpointFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method))
        {
            // Throws AntiforgeryValidationException, mapped to 400 csrf_token_invalid.
            await antiforgery.ValidateRequestAsync(context.HttpContext).ConfigureAwait(false);
        }

        return await next(context).ConfigureAwait(false);
    }
}

internal static class LocalReturnUrl
{
    /// <summary>
    /// Accepts only same-site absolute paths ("/account"); anything else, including
    /// protocol-relative ("//evil.example") or backslash tricks, becomes "/".
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 2048 || value[0] != '/'
            || (value.Length > 1 && (value[1] == '/' || value[1] == '\\'))
            || value.Any(c => char.IsControl(c) || c == '\\'))
        {
            return "/";
        }

        return value;
    }
}

internal static class ClientDescription
{
    /// <summary>A coarse, human-readable client label ("Firefox on Windows"); the raw user agent is not stored.</summary>
    public static string FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "Unknown browser";
        }

        var browser = userAgent switch
        {
            _ when userAgent.Contains("Edg/", StringComparison.Ordinal) => "Edge",
            _ when userAgent.Contains("OPR/", StringComparison.Ordinal) => "Opera",
            _ when userAgent.Contains("Firefox/", StringComparison.Ordinal) => "Firefox",
            _ when userAgent.Contains("Chrome/", StringComparison.Ordinal) => "Chrome",
            _ when userAgent.Contains("Safari/", StringComparison.Ordinal) => "Safari",
            _ => "Browser",
        };
        var os = userAgent switch
        {
            _ when userAgent.Contains("Android", StringComparison.Ordinal) => "Android",
            _ when userAgent.Contains("iPhone", StringComparison.Ordinal) || userAgent.Contains("iPad", StringComparison.Ordinal) => "iOS",
            _ when userAgent.Contains("Windows", StringComparison.Ordinal) => "Windows",
            _ when userAgent.Contains("Mac OS X", StringComparison.Ordinal) => "macOS",
            _ when userAgent.Contains("Linux", StringComparison.Ordinal) => "Linux",
            _ => null,
        };
        return os is null ? browser : $"{browser} on {os}";
    }
}
