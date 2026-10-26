using System.Text.Json;
using CoachDB.Api.Common.Results;
using CoachDB.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CoachDB.Api.Tests;

/// <summary>Executes <see cref="ResultExtensions.ToProblem"/> results against the host's real services and reads the JSON written.</summary>
public sealed class ProblemMappingTests(CoachDbApiFactory factory) : IClassFixture<CoachDbApiFactory>
{
    private async Task<(int Status, string? ContentType, JsonElement Body)> ExecuteAsync(IResult result)
    {
        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            TraceIdentifier = "trace-xyz",
        };
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        return (context.Response.StatusCode, context.Response.ContentType, doc.RootElement.Clone());
    }

    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Failure, 500)]
    public async Task Error_type_maps_to_status_code_and_type_url(ErrorType type, int expectedStatus)
    {
        var error = new Error("thing_went_wrong", "Thing went wrong.", type);

        var (status, contentType, body) = await ExecuteAsync(error.ToProblem());

        status.ShouldBe(expectedStatus);
        contentType.ShouldBe("application/problem+json");
        body.GetProperty("status").GetInt32().ShouldBe(expectedStatus);
        body.GetProperty("code").GetString().ShouldBe("thing_went_wrong");
        body.GetProperty("type").GetString().ShouldBe("https://coachdb.app/errors/thing_went_wrong");
        body.GetProperty("title").GetString().ShouldBe("Thing went wrong.");
        body.GetProperty("traceId").GetString().ShouldBe("trace-xyz");
    }

    [Fact]
    public async Task Validation_error_includes_field_errors()
    {
        var error = new ValidationError(new Dictionary<string, string[]>
        {
            ["price.amount"] = ["must_be_positive"],
            ["name"] = ["required"],
        });

        var (status, _, body) = await ExecuteAsync(error.ToProblem());

        status.ShouldBe(400);
        body.GetProperty("code").GetString().ShouldBe("validation_failed");
        var errors = body.GetProperty("errors");
        errors.GetProperty("price.amount")[0].GetString().ShouldBe("must_be_positive");
        errors.GetProperty("name")[0].GetString().ShouldBe("required");
    }

    [Fact]
    public async Task Success_result_maps_to_ok_with_value()
    {
        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Response.Body = new MemoryStream();

        await Result.Success(new { Name = "Cut" }).ToOk().ExecuteAsync(context);

        context.Response.StatusCode.ShouldBe(200);
        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        doc.RootElement.GetProperty("name").GetString().ShouldBe("Cut");
    }

    [Fact]
    public void Error_none_cannot_become_a_problem()
    {
        Should.Throw<InvalidOperationException>(() => Error.None.ToProblem());
    }
}
