using Audorix.ChatX.Application.Abstractions;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Contracts.Errors;
using Audorix.ChatX.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Audorix.ChatX.Api.Errors;

/// <summary>Maps exceptions to problem responses with stable codes.</summary>
internal sealed class ChatXExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<ChatXExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing useful can be written.
            logger.LogDebug("Request {Method} {Path} was cancelled by the client.", httpContext.Request.Method, httpContext.Request.Path);
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var (status, code) = exception switch
        {
            RequestValidationException => (StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed),
            Setup.SetupException setup => (setup.Status, setup.Code),
            CaptchaRejectedException { ProviderUnavailable: true } => (StatusCodes.Status503ServiceUnavailable, ErrorCodes.CaptchaUnavailable),
            CaptchaRejectedException => (StatusCodes.Status400BadRequest, ErrorCodes.CaptchaFailed),
            FeatureDisabledException disabled => (StatusCodes.Status403Forbidden, disabled.Code),
            ResourceNotFoundException notFound => (StatusCodes.Status404NotFound, notFound.Code),
            ForbiddenException forbidden => (StatusCodes.Status403Forbidden, forbidden.Code),
            ConflictException conflict => (StatusCodes.Status409Conflict, conflict.Code),
            PayloadTooLargeException tooLarge => (StatusCodes.Status413PayloadTooLarge, tooLarge.Code),
            Application.Widgets.UnauthorizedVisitorException visitor => (StatusCodes.Status401Unauthorized, visitor.Code),
            Application.Widgets.TooManyRequestsException => (StatusCodes.Status429TooManyRequests, ErrorCodes.TooManyRequests),
            DependencyUnavailableException unavailable => (StatusCodes.Status503ServiceUnavailable, unavailable.Code),
            Application.Attachments.StorageUnavailableException => (StatusCodes.Status503ServiceUnavailable, "attachment_storage_unavailable"),
            ApplicationRuleException rule => (StatusCodes.Status400BadRequest, rule.Code),
            DomainRuleException rule => (StatusCodes.Status400BadRequest, rule.Code),
            AntiforgeryValidationException => (StatusCodes.Status400BadRequest, ErrorCodes.CsrfTokenInvalid),
            EmailDeliveryException => (StatusCodes.Status503ServiceUnavailable, ErrorCodes.EmailUnavailable),
            BadHttpRequestException bad => (bad.StatusCode, ErrorCodes.BadRequest),
            _ => (StatusCodes.Status500InternalServerError, ErrorCodes.InternalError),
        };

        if (status >= 500 && exception is not EmailDeliveryException and not CaptchaRejectedException and not Setup.SetupException
            and not DependencyUnavailableException)
        {
            // Server-side log keeps the exception for diagnosis; the response never does.
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request rejected with {StatusCode} {Code}.", status, code);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Extensions = { [ProblemDetailsSetup.CodeKey] = code },
        };
        if (exception is RequestValidationException validation)
        {
            problem.Extensions["errors"] = validation.Errors;
        }
        else if (exception is Setup.SetupException { Errors: not null } withErrors)
        {
            problem.Extensions["errors"] = withErrors.Errors;
        }

        if (exception is Setup.SetupException { Details: { Count: > 0 } details })
        {
            problem.Extensions["details"] = details;
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem }).ConfigureAwait(false);
    }
}
