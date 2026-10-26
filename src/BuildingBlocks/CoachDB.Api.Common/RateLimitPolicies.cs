namespace CoachDB.Api.Common;

/// <summary>Rate-limit policy names; the host defines the limits, endpoints opt in with <c>RequireRateLimiting</c>.</summary>
public static class RateLimitPolicies
{
    /// <summary>300 requests/min per user, or per IP when anonymous. Applied to the whole <c>/api/v1</c> group.</summary>
    public const string Api = "api";

    /// <summary>5 requests/min per IP. Login, register, refresh and password reset.</summary>
    public const string Auth = "auth";
}
