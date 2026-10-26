using CoachDB.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CoachDB.Api.Common.Results;

/// <summary>Turns a handler's <see cref="Result"/> into an HTTP response. The only place error types become status codes.</summary>
public static class ResultExtensions
{
    public const string ErrorTypeBaseUri = "https://coachdb.app/errors/";

    /// <summary>Success → <paramref name="onSuccess"/>; failure → RFC 7807 problem details.</summary>
    public static IResult Match<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToProblem();

    /// <summary>Success → <paramref name="onSuccess"/> (usually 204); failure → problem details.</summary>
    public static IResult Match(this Result result, Func<IResult> onSuccess) =>
        result.IsSuccess ? onSuccess() : result.Error.ToProblem();

    /// <summary>200 with the value, or problem details.</summary>
    public static IResult ToOk<T>(this Result<T> result) => result.Match(value => TypedResults.Ok(value));

    /// <summary>204, or problem details.</summary>
    public static IResult ToNoContent(this Result result) => result.Match(() => TypedResults.NoContent());

    public static ProblemHttpResult ToProblem(this Error error)
    {
        if (error == Error.None)
            throw new InvalidOperationException("Cannot build problem details for a successful result.");

        var status = StatusCodeFor(error.Type);
        ProblemDetails problem = error is ValidationError validation
            ? new HttpValidationProblemDetails(validation.Errors.ToDictionary(kv => kv.Key, kv => kv.Value))
            : new ProblemDetails();

        problem.Status = status;
        problem.Type = ErrorTypeBaseUri + error.Code;
        problem.Title = error.Description;
        problem.Extensions[ProblemDetailsSetup.CodeKey] = error.Code;

        return TypedResults.Problem(problem);
    }

    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };
}
