using CoachDB.SharedKernel.Results;

namespace CoachDB.Identity.Domain;

/// <summary>Every expected failure of the Identity module, with its stable code.</summary>
public static class IdentityErrors
{
    public static readonly Error InvalidEmail =
        Error.Validation("email_invalid", "The email address is not valid.");

    public static readonly Error EmailTaken =
        Error.Conflict("email_taken", "An account with this email already exists.");

    // Same error for unknown email and wrong password, so the API doesn't reveal which accounts exist.
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("invalid_credentials", "Email or password is incorrect.");

    public static readonly Error RefreshTokenInvalid =
        Error.Unauthorized("refresh_token_invalid", "The session has expired. Sign in again.");

    public static readonly Error RefreshTokenReused =
        Error.Unauthorized("refresh_token_reused", "This session was signed out for safety. Sign in again.");

    public static readonly Error UserNotFound =
        Error.NotFound("user_not_found", "The user was not found.");
}
