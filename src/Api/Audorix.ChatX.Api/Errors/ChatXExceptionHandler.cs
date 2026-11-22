using Audorix.ChatX.Contracts.Errors;
using Audorix.ChatX.Domain;
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
            DomainRuleException rule => (StatusCodes.Status400BadRequest, rule.Code),
            BadHttpRequestException bad => (bad.StatusCode, ErrorCodes.BadRequest),
            _ => (StatusCodes.Status500InternalServerError, ErrorCodes.InternalError),
        };

        if (status >= 500)
        {
            // Server-side log keeps the exception for diagnosis; the response never does.
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request rejected with {StatusCode} {Code}.", status, code);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Extensions = { [ProblemDetailsSetup.CodeKey] = code },
            },
        }).ConfigureAwait(false);
    }
}
