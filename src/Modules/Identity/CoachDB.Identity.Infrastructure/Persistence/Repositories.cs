using CoachDB.Identity.Application.Abstractions;
using CoachDB.Identity.Contracts;
using CoachDB.Identity.Domain.Sessions;
using CoachDB.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CoachDB.Identity.Infrastructure.Persistence;

internal sealed class UserRepository(IdentityDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

    // IgnoreQueryFilters: a soft-deleted account still holds its email (the column is unique).
    public Task<bool> EmailExistsAsync(Email email, CancellationToken cancellationToken) =>
        db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, cancellationToken);

    public void Add(User user) => db.Users.Add(user);
}

internal sealed class RefreshTokenRepository(IdentityDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);

    public Task RevokeFamilyAsync(Guid familyId, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, reason), cancellationToken);
}

/// <summary>Implements the public <see cref="IUserDirectory"/> contract for other modules (read-only, no tracking).</summary>
internal sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public Task<UserSummary?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(u => u.Id == new UserId(userId))
            .Select(u => new UserSummary(u.Id.Value, u.Email.Value, u.FullName, u.AvatarUrl, u.Locale))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<UserSummary?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var parsed = Email.Create(email);
        if (parsed.IsFailure)
            return null;

        return await db.Users.AsNoTracking()
            .Where(u => u.Email == parsed.Value)
            .Select(u => new UserSummary(u.Id.Value, u.Email.Value, u.FullName, u.AvatarUrl, u.Locale))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
