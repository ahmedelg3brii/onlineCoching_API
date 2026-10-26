using System.Reflection;
using CoachDB.Identity.Application.Abstractions;
using CoachDB.Identity.Application.Auth;
using CoachDB.Identity.Domain.Sessions;
using CoachDB.Identity.Domain.Users;
using CoachDB.Identity.Infrastructure.Security;
using CoachDB.Infrastructure.Common.Security;
using Microsoft.Extensions.Options;

namespace CoachDB.Identity.UnitTests;

internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeUserRepository : IUserRepository
{
    public List<User> Users { get; } = [];

    public Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(u => u.Id == id));

    public Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(u => u.Email == email));

    public Task<bool> EmailExistsAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(Users.Any(u => u.Email == email));

    public void Add(User user) => Users.Add(user);
}

internal sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
{
    public List<RefreshToken> Tokens { get; } = [];
    public List<(Guid FamilyId, string Reason)> RevokedFamilies { get; } = [];

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(Tokens.SingleOrDefault(t => t.TokenHash == tokenHash));

    public void Add(RefreshToken token) => Tokens.Add(token);

    /// <summary>Mirrors the EF ExecuteUpdate: revokes every still-active token of the family.</summary>
    public Task RevokeFamilyAsync(Guid familyId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        RevokedFamilies.Add((familyId, reason));
        foreach (var token in Tokens.Where(t => t.FamilyId == familyId && t.RevokedAt is null))
            ForceRevoke(token, reason, now);
        return Task.CompletedTask;
    }

    // RefreshToken has private setters; the real repository bypasses them with ExecuteUpdate.
    public static void ForceRevoke(RefreshToken token, string reason, DateTimeOffset now)
    {
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.RevokedAt))!.SetValue(token, now);
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.RevokedReason))!.SetValue(token, reason);
    }
}

/// <summary>Real Infrastructure services wired with fakes, as the handlers see them in production.</summary>
internal sealed class IdentityFixture
{
    public static readonly DateTimeOffset Start = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    public FakeClock Clock { get; } = new(Start);
    public FakeUserRepository Users { get; } = new();
    public FakeRefreshTokenRepository Tokens { get; } = new();
    public IOptions<JwtOptions> Jwt { get; } = Options.Create(new JwtOptions
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        SigningKey = "unit-test-signing-key-0123456789abcdef",
    });

    public PasswordHasher Hasher { get; } = new();
    public RefreshTokenGenerator Generator { get; }
    public JwtAccessTokenIssuer AccessTokens { get; }
    public SessionIssuer Sessions { get; }

    public IdentityFixture()
    {
        Generator = new RefreshTokenGenerator(Jwt);
        AccessTokens = new JwtAccessTokenIssuer(Jwt, Clock);
        Sessions = new SessionIssuer(AccessTokens, Generator, Tokens, Clock);
    }

    public User AddUser(string email = "coach@example.com", string password = "Secret123!", string? locale = null)
    {
        var user = User.Register(Email.Create(email).Value, "Test User", locale, Hasher.Hash(password), Clock.Now);
        user.ClearDomainEvents();
        Users.Add(user);
        return user;
    }

    /// <summary>Starts a session and returns the plain refresh token the client would hold.</summary>
    public (string Plain, RefreshToken Stored) StartSession(User user)
    {
        var result = Sessions.StartSession(user);
        return (result.RefreshToken, Tokens.Tokens.Last());
    }

    public static void MakePlatformAdmin(User user) =>
        typeof(User).GetProperty(nameof(User.IsPlatformAdmin), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(user, true);
}
