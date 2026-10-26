using CoachDB.Identity.Application.Auth;
using CoachDB.Identity.Application.Users;

namespace CoachDB.Identity.Api.Auth;

// HTTP request/response bodies. They are separate from commands so the wire format can stay stable
// while use cases change.

public sealed record RegisterRequest(string Email, string Password, string FullName, string? Locale);

public sealed record LoginRequest(string Email, string Password);

/// <summary>Optional body for clients without cookies (future native app). Browsers send the cookie instead.</summary>
public sealed record RefreshRequest(string? RefreshToken);

/// <summary>Returned by register, login and refresh. The refresh token is only ever in the HttpOnly cookie.</summary>
public sealed record AuthResponse(string AccessToken, string TokenType, int ExpiresIn, DateTimeOffset ExpiresAt, UserResponse User)
{
    public static AuthResponse From(AuthResult result, DateTimeOffset now) => new(
        result.AccessToken,
        "Bearer",
        (int)Math.Max(0, (result.AccessTokenExpiresAt - now).TotalSeconds),
        result.AccessTokenExpiresAt,
        result.User);
}
