using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CoachDB.Api.Common.Results;

/// <summary>
/// Last line of defence. Exceptions mean bugs or infrastructure failures (expected failures are Results),
/// so the client gets a 500 with only a code and trace id; details stay in the logs.
/// Malformed request bodies (<see cref="BadHttpRequestException"/>) become 400 <c>invalid_request</c>.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        if (exception is BadHttpRequestException badRequest)
        {
            LogBadRequest(logger, badRequest.Message);
            problem = new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = "The request could not be read.",
                Type = ResultExtensions.ErrorTypeBaseUri + "invalid_request",
                Extensions = { [ProblemDetailsSetup.CodeKey] = "invalid_request" },
            };
        }
        else
        {
            LogUnhandled(logger, exception);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = ResultExtensions.ErrorTypeBaseUri + "internal_error",
                Extensions = { [ProblemDetailsSetup.CodeKey] = "internal_error" },
            };
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bad request: {Reason}")]
    private static partial void LogBadRequest(ILogger logger, string reason);
}
