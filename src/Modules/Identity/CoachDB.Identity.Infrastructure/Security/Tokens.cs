using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CoachDB.Identity.Application.Abstractions;
using CoachDB.Identity.Domain.Users;
using CoachDB.Infrastructure.Common.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using AspNetPasswordHasher = Microsoft.AspNetCore.Identity.PasswordHasher<CoachDB.Identity.Domain.Users.User>;
using AspNetVerification = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace CoachDB.Identity.Infrastructure.Security;

/// <summary>PBKDF2 hashing from ASP.NET Core Identity (versioned format, so parameters can be raised later).</summary>
internal sealed class PasswordHasher : IPasswordHasher
{
    private static readonly AspNetPasswordHasher Inner = new();

    public string Hash(string password) => Inner.HashPassword(null!, password);

    public PasswordCheck Verify(string passwordHash, string password) =>
        Inner.VerifyHashedPassword(null!, passwordHash, password) switch
        {
            AspNetVerification.Success => PasswordCheck.Success,
            AspNetVerification.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed,
        };
}

/// <summary>
/// Short-lived (15 min) HMAC-SHA256 access tokens. Claims: <c>sub</c>, <c>email</c>, <c>name</c>, and
/// <c>padm</c> for platform admins. Tenant permissions are not in the token; they are loaded per request.
/// </summary>
internal sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock) : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken Issue(User user)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email.Value),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };
        if (user.IsPlatformAdmin)
            claims.Add(new Claim("padm", "true"));

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expires);
    }
}

/// <summary>256-bit random refresh tokens (base64url); the database stores only their SHA-256 hex hash.</summary>
internal sealed class RefreshTokenGenerator(IOptions<JwtOptions> options) : IRefreshTokenGenerator
{
    public TimeSpan Lifetime => TimeSpan.FromDays(options.Value.RefreshTokenDays);

    public (string Token, string Hash) Create()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
