using CoachDB.Identity.Domain.Users;
using CoachDB.SharedKernel.Domain;

namespace CoachDB.Identity.Domain.Sessions;

/// <summary>
/// One link in a rotating refresh-token chain. Only a SHA-256 hash is stored. Every refresh revokes the
/// presented token and issues the next one in the same <see cref="FamilyId"/>; presenting a token that was
/// already rotated means it was stolen, so the whole family (the session) is revoked.
/// </summary>
public sealed class RefreshToken : Entity<Guid>
{
    public const string RotatedReason = "rotated";
    public const string ReuseDetectedReason = "reuse_detected";
    public const string LogoutReason = "logout";

    private RefreshToken() { } // EF Core

    private RefreshToken(Guid id, UserId userId, Guid familyId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
        : base(id)
    {
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public UserId UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }

    /// <summary>Starts a new session (a new family).</summary>
    public static RefreshToken StartSession(UserId userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        new(Guid.CreateVersion7(), userId, Guid.CreateVersion7(), tokenHash, now, now + lifetime);

    public bool IsRevoked => RevokedAt.HasValue;

    /// <summary>
    /// Revoked because it was exchanged for a successor. Seeing such a token again means it was copied:
    /// theft. A token revoked by logout or reuse handling is just invalid.
    /// </summary>
    public bool WasRotated => RevokedReason == RotatedReason;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Revokes this token and returns its successor in the same family. Caller checks it is active first.</summary>
    public RefreshToken Rotate(string newTokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        if (IsRevoked || IsExpired(now))
            throw new InvalidOperationException("Only an active refresh token can be rotated.");

        var next = new RefreshToken(Guid.CreateVersion7(), UserId, FamilyId, newTokenHash, now, now + lifetime);
        RevokedAt = now;
        RevokedReason = RotatedReason;
        ReplacedByTokenId = next.Id;
        return next;
    }
}
