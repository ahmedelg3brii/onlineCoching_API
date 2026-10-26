using CoachDB.SharedKernel.Results;

namespace CoachDB.BuildingBlocks.UnitTests.Results;

public sealed class ResultTests
{
    private static readonly Error SomeError = Error.NotFound("thing_not_found", "Thing not found.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        var result = Result.Failure(SomeError);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SomeError);
    }

    [Fact]
    public void Failure_without_an_error_is_rejected()
    {
        Should.Throw<ArgumentException>(() => Result.Failure(Error.None));
        Should.Throw<ArgumentException>(() => Result.Failure<int>(Error.None));
    }

    [Fact]
    public void Generic_success_exposes_the_value()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws()
    {
        var result = Result.Failure<int>(SomeError);

        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Implicit_conversion_from_value_is_success()
    {
        Result<string> result = "hello";

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("hello");
    }

    [Fact]
    public void Implicit_conversion_from_error_is_failure()
    {
        Result<string> generic = SomeError;
        Result plain = SomeError;

        generic.IsFailure.ShouldBeTrue();
        generic.Error.ShouldBe(SomeError);
        plain.IsFailure.ShouldBeTrue();
        plain.Error.ShouldBe(SomeError);
    }

    [Fact]
    public void Validation_error_has_stable_code_and_type()
    {
        var error = new ValidationError(new Dictionary<string, string[]> { ["name"] = ["required"] });

        error.Code.ShouldBe("validation_failed");
        error.Type.ShouldBe(ErrorType.Validation);
        error.Errors["name"].ShouldBe(["required"]);
    }

    [Theory]
    [InlineData(ErrorType.Failure)]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    public void Error_factories_set_the_type(ErrorType type)
    {
        var error = type switch
        {
            ErrorType.Validation => Error.Validation("c", "d"),
            ErrorType.Unauthorized => Error.Unauthorized("c", "d"),
            ErrorType.Forbidden => Error.Forbidden("c", "d"),
            ErrorType.NotFound => Error.NotFound("c", "d"),
            ErrorType.Conflict => Error.Conflict("c", "d"),
            _ => Error.Failure("c", "d"),
        };

        error.Type.ShouldBe(type);
        error.Code.ShouldBe("c");
    }
}
