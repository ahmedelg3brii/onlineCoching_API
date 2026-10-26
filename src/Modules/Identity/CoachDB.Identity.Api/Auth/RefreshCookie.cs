using CoachDB.Identity.Application.Auth;
using Microsoft.AspNetCore.Http;

namespace CoachDB.Identity.Api.Auth;

/// <summary>
/// The refresh token lives in an HttpOnly, Secure, SameSite=Strict cookie scoped to <c>/api/v1/auth</c>:
/// JavaScript can't read it and the browser sends it only to the auth endpoints.
/// </summary>
internal static class RefreshCookie
{
    public const string Name = "coachdb_rt";
    public const string Path = "/api/v1/auth";

    public static void Set(HttpResponse response, AuthResult result) =>
        response.Cookies.Append(Name, result.RefreshToken, Options(result.RefreshTokenExpiresAt));

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(expires: null));

    /// <summary>Cookie first; body as a fallback for clients without cookies.</summary>
    public static string? Read(HttpRequest request, RefreshRequest? body) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value)
            ? value
            : body?.RefreshToken;

    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true, // browsers accept Secure cookies on http://localhost during development
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expires,
        IsEssential = true,
    };
}
