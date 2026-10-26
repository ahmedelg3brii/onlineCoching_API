using CoachDB.Identity.Domain.Sessions;
using CoachDB.Identity.Domain.Users;

namespace CoachDB.Identity.Application.Abstractions;

// What the use cases need from the outside world. Implemented in CoachDB.Identity.Infrastructure.

public interface IUserRepository
{
    Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken);
    Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken);
    Task<bool> EmailExistsAsync(Email email, CancellationToken cancellationToken);
    void Add(User user);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);
    void Add(RefreshToken token);

    /// <summary>Revokes every active token of a session immediately (not deferred to the unit of work),
    /// so it sticks even when the command then returns a failure.</summary>
    Task RevokeFamilyAsync(Guid familyId, string reason, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordCheck Verify(string passwordHash, string password);
}

public enum PasswordCheck
{
    Failed,
    Success,
    SuccessRehashNeeded,
}

public interface IAccessTokenIssuer
{
    AccessToken Issue(User user);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Creates opaque refresh tokens; only their hash is stored.</summary>
public interface IRefreshTokenGenerator
{
    TimeSpan Lifetime { get; }

    (string Token, string Hash) Create();

    string Hash(string token);
}
