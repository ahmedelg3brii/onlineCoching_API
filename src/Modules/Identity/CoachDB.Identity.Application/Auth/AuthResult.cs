using CoachDB.Identity.Application.Abstractions;
using CoachDB.Identity.Application.Users;
using CoachDB.Identity.Domain.Sessions;
using CoachDB.Identity.Domain.Users;

namespace CoachDB.Identity.Application.Auth;

/// <summary>
/// What a successful sign-in produces. The endpoint returns the access token and user in the body and puts
/// the refresh token in an HttpOnly cookie, so browser JavaScript never sees it.
/// </summary>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserResponse User);

/// <summary>Issues the access token and stores the refresh token. Shared by register, login and refresh.</summary>
internal sealed class SessionIssuer(
    IAccessTokenIssuer accessTokens,
    IRefreshTokenGenerator refreshTokens,
    IRefreshTokenRepository repository,
    TimeProvider clock)
{
    public AuthResult StartSession(User user)
    {
        var (token, hash) = refreshTokens.Create();
        var refresh = RefreshToken.StartSession(user.Id, hash, clock.GetUtcNow(), refreshTokens.Lifetime);
        repository.Add(refresh);
        return Build(user, token, refresh);
    }

    public AuthResult Continue(User user, RefreshToken current)
    {
        var (token, hash) = refreshTokens.Create();
        var next = current.Rotate(hash, clock.GetUtcNow(), refreshTokens.Lifetime);
        repository.Add(next);
        return Build(user, token, next);
    }

    private AuthResult Build(User user, string refreshToken, RefreshToken stored)
    {
        var access = accessTokens.Issue(user);
        return new AuthResult(access.Token, access.ExpiresAt, refreshToken, stored.ExpiresAt, UserResponse.From(user));
    }
}
