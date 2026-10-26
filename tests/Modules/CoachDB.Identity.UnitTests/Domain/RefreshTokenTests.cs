using CoachDB.Identity.Domain.Sessions;
using CoachDB.Identity.Domain.Users;

namespace CoachDB.Identity.UnitTests.Domain;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = IdentityFixture.Start;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    private static readonly UserId Owner = UserId.New();

    [Fact]
    public void StartSession_creates_an_active_token_in_a_new_family()
    {
        var a = RefreshToken.StartSession(Owner, "hash-a", Now, Lifetime);
        var b = RefreshToken.StartSession(Owner, "hash-b", Now, Lifetime);

        a.UserId.ShouldBe(Owner);
        a.TokenHash.ShouldBe("hash-a");
        a.CreatedAt.ShouldBe(Now);
        a.ExpiresAt.ShouldBe(Now + Lifetime);
        a.IsRevoked.ShouldBeFalse();
        a.WasRotated.ShouldBeFalse();
        a.FamilyId.ShouldNotBe(b.FamilyId);
        a.Id.ShouldNotBe(b.Id);
    }

    [Fact]
    public void Rotate_revokes_the_current_token_and_links_a_successor_in_the_same_family()
    {
        var current = RefreshToken.StartSession(Owner, "hash-a", Now, Lifetime);
        var later = Now.AddDays(1);

        var next = current.Rotate("hash-b", later, Lifetime);

        current.IsRevoked.ShouldBeTrue();
        current.RevokedAt.ShouldBe(later);
        current.RevokedReason.ShouldBe(RefreshToken.RotatedReason);
        current.WasRotated.ShouldBeTrue();
        current.ReplacedByTokenId.ShouldBe(next.Id);

        next.Id.ShouldNotBe(current.Id);
        next.FamilyId.ShouldBe(current.FamilyId);
        next.UserId.ShouldBe(Owner);
        next.TokenHash.ShouldBe("hash-b");
        next.ExpiresAt.ShouldBe(later + Lifetime);
        next.IsRevoked.ShouldBeFalse();
    }

    [Fact]
    public void Rotate_on_a_revoked_token_throws()
    {
        var current = RefreshToken.StartSession(Owner, "hash-a", Now, Lifetime);
        current.Rotate("hash-b", Now, Lifetime);

        Should.Throw<InvalidOperationException>(() => current.Rotate("hash-c", Now, Lifetime));
    }

    [Fact]
    public void Rotate_on_an_expired_token_throws()
    {
        var current = RefreshToken.StartSession(Owner, "hash-a", Now, Lifetime);

        Should.Throw<InvalidOperationException>(() => current.Rotate("hash-b", Now + Lifetime, Lifetime));
    }

    [Fact]
    public void IsExpired_is_true_from_ExpiresAt_onwards()
    {
        var token = RefreshToken.StartSession(Owner, "hash-a", Now, Lifetime);

        token.IsExpired(token.ExpiresAt.AddTicks(-1)).ShouldBeFalse();
        token.IsExpired(token.ExpiresAt).ShouldBeTrue();
        token.IsExpired(token.ExpiresAt.AddTicks(1)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(RefreshToken.LogoutReason)]
    [InlineData(RefreshToken.ReuseDetectedReason)]
    public void WasRotated_is_false_for_other_revocations(string reason)
    {
        var token = RefreshToken.StartSession(Owner, "hash-a", Now, Lifetime);

        FakeRefreshTokenRepository.ForceRevoke(token, reason, Now);

        token.IsRevoked.ShouldBeTrue();
        token.WasRotated.ShouldBeFalse();
    }
}
