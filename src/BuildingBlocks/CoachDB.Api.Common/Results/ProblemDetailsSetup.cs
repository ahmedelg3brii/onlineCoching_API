using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CoachDB.Api.Common.Results;

/// <summary>
/// Makes every error response the same RFC 7807 shape, whether it comes from a handler, the framework
/// (404 route, 405, auth challenge) or an unhandled exception:
/// <c>{ type, title, status, code, traceId, errors? }</c>.
/// </summary>
public static class ProblemDetailsSetup
{
    public const string CodeKey = "code";
    public const string TraceIdKey = "traceId";

    public static IServiceCollection AddCoachDbProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;

            if (!problem.Extensions.ContainsKey(CodeKey))
            {
                var code = DefaultCodeFor(status);
                problem.Extensions[CodeKey] = code;
                problem.Type = ResultExtensions.ErrorTypeBaseUri + code;
            }

            // Same value as the X-Correlation-Id header and the CorrelationId on every log line.
            problem.Extensions[TraceIdKey] = context.HttpContext.TraceIdentifier;
            problem.Instance = null; // never echo internal paths
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }

    /// <summary>Code for responses the framework produced without a domain error.</summary>
    public static string DefaultCodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "bad_request",
        StatusCodes.Status401Unauthorized => "auth_required",
        StatusCodes.Status403Forbidden => "permission_denied",
        StatusCodes.Status404NotFound => "not_found",
        StatusCodes.Status405MethodNotAllowed => "method_not_allowed",
        StatusCodes.Status409Conflict => "conflict",
        StatusCodes.Status413PayloadTooLarge => "payload_too_large",
        StatusCodes.Status415UnsupportedMediaType => "unsupported_media_type",
        StatusCodes.Status429TooManyRequests => "rate_limited",
        _ => "internal_error",
    };
}
