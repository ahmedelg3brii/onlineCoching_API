namespace CoachDB.SharedKernel.Results;

/// <summary>
/// An expected failure. <see cref="Code"/> is a stable snake_case identifier the frontend translates
/// (e.g. <c>package_not_found</c>); <see cref="Description"/> is an English fallback for logs and developers.
/// </summary>
public record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
}

/// <summary>Validation failure carrying per-field error codes, e.g. <c>{ "price.amount": ["must_be_positive"] }</c>.</summary>
public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors)
    : Error(ValidationError.ValidationCode, "One or more fields are invalid.", ErrorType.Validation)
{
    public const string ValidationCode = "validation_failed";
}
