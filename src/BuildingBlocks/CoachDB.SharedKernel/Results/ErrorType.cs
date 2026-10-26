namespace CoachDB.SharedKernel.Results;

/// <summary>Category of an expected failure. The API layer maps each one to an HTTP status code.</summary>
public enum ErrorType
{
    Failure = 0,      // 500
    Validation = 1,   // 400
    Unauthorized = 2, // 401
    Forbidden = 3,    // 403
    NotFound = 4,     // 404
    Conflict = 5,     // 409
}
