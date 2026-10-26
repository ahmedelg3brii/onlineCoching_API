namespace CoachDB.Infrastructure.Common.Security;

/// <summary>JWT settings shared by the issuer (Identity module) and the validator (API host). Section <c>Jwt</c>.</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;

    /// <summary>HMAC key, at least 32 bytes. Comes from user-secrets locally and the vault in staging/production.</summary>
    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
}
