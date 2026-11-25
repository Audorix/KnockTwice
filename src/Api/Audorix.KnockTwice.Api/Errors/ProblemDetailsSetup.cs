using System.Diagnostics;
using Audorix.KnockTwice.Api.Localization;
using Audorix.KnockTwice.Contracts.Errors;
using Microsoft.Extensions.Localization;

namespace Audorix.KnockTwice.Api.Errors;

/// <summary>
/// Every error response is an RFC 9457 problem document with a stable <c>code</c> and a
/// <c>traceId</c> for support correlation. Titles are localized from resource files.
/// Exception messages and stack traces are never included.
/// </summary>
internal static class ProblemDetailsSetup
{
    public const string CodeKey = "code";
    public const string TraceIdKey = "traceId";

    public static IServiceCollection AddChatXProblemDetails(this IServiceCollection services)
    {
        services.AddExceptionHandler<ChatXExceptionHandler>();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var code = problem.Extensions.TryGetValue(CodeKey, out var value) && value is string s
                ? s
                : CodeForStatus(problem.Status ?? context.HttpContext.Response.StatusCode);

            problem.Extensions[CodeKey] = code;
            problem.Extensions[TraceIdKey] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            problem.Extensions.Remove("exception");

            var localizer = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<ErrorMessages>>();
            var title = localizer[code];
            if (title.ResourceNotFound)
            {
                title = localizer[ErrorCodes.DomainRuleViolation];
            }

            problem.Title = title;
            problem.Detail = null;
        });
        return services;
    }

    private static string CodeForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ErrorCodes.BadRequest,
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthorized,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status429TooManyRequests => ErrorCodes.TooManyRequests,
        StatusCodes.Status405MethodNotAllowed => ErrorCodes.MethodNotAllowed,
        StatusCodes.Status409Conflict => ErrorCodes.Conflict,
        StatusCodes.Status415UnsupportedMediaType => ErrorCodes.UnsupportedMediaType,
        StatusCodes.Status422UnprocessableEntity => ErrorCodes.ValidationFailed,
        StatusCodes.Status503ServiceUnavailable => ErrorCodes.ServiceUnavailable,
        >= 500 => ErrorCodes.InternalError,
        _ => ErrorCodes.BadRequest,
    };
}
