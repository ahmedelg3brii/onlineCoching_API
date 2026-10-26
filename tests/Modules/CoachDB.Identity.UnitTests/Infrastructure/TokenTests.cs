using CoachDB.Identity.Application.Abstractions;
using CoachDB.Identity.Infrastructure.Security;
using CoachDB.Infrastructure.Common.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CoachDB.Identity.UnitTests.Infrastructure;

public sealed class JwtAccessTokenIssuerTests
{
    private readonly IdentityFixture _f = new();

    [Fact]
    public void Issues_a_15_minute_token_with_identity_claims()
    {
        var user = _f.AddUser("coach@example.com");

        var access = _f.AccessTokens.Issue(user);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(access.Token);

        access.ExpiresAt.ShouldBe(IdentityFixture.Start.AddMinutes(15));
        jwt.Issuer.ShouldBe("test-issuer");
        jwt.Audiences.ShouldBe(["test-audience"]);
        jwt.Subject.ShouldBe(user.Id.Value.ToString());
        jwt.GetClaim(JwtRegisteredClaimNames.Email).Value.ShouldBe("coach@example.com");
        jwt.GetClaim(JwtRegisteredClaimNames.Name).Value.ShouldBe("Test User");
        Guid.TryParse(jwt.Id, out _).ShouldBeTrue();
        jwt.ValidTo.ShouldBe(IdentityFixture.Start.AddMinutes(15).UtcDateTime);
        jwt.Alg.ShouldBe("HS256");
        jwt.TryGetClaim("padm", out _).ShouldBeFalse();
    }

    [Fact]
    public void Adds_padm_only_for_platform_admins()
    {
        var admin = _f.AddUser("admin@example.com");
        IdentityFixture.MakePlatformAdmin(admin); // private setter → reflection

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(_f.AccessTokens.Issue(admin).Token);

        jwt.GetClaim("padm").Value.ShouldBe("true");
    }

    [Fact]
    public void Each_token_has_a_unique_id()
    {
        var user = _f.AddUser();
        var handler = new JsonWebTokenHandler();

        handler.ReadJsonWebToken(_f.AccessTokens.Issue(user).Token).Id
            .ShouldNotBe(handler.ReadJsonWebToken(_f.AccessTokens.Issue(user).Token).Id);
    }
}

public sealed class RefreshTokenGeneratorTests
{
    private static readonly RefreshTokenGenerator Generator =
        new(Options.Create(new JwtOptions { RefreshTokenDays = 7 }));

    [Fact]
    public void Hash_is_64_char_lowercase_hex_and_deterministic()
    {
        var hash = Generator.Hash("some-token");

        hash.Length.ShouldBe(64);
        hash.ShouldAllBe(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'));
        Generator.Hash("some-token").ShouldBe(hash);
        Generator.Hash("other-token").ShouldNotBe(hash);
    }

    [Fact]
    public void Create_returns_distinct_url_safe_tokens_with_matching_hashes()
    {
        var (token1, hash1) = Generator.Create();
        var (token2, hash2) = Generator.Create();

        token1.ShouldNotBe(token2);
        hash1.ShouldBe(Generator.Hash(token1));
        hash2.ShouldBe(Generator.Hash(token2));
        token1.ShouldAllBe(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
        token1.Length.ShouldBe(43); // 32 bytes, base64url without padding
    }

    [Fact]
    public void Lifetime_comes_from_RefreshTokenDays() => Generator.Lifetime.ShouldBe(TimeSpan.FromDays(7));
}

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Verifies_the_right_password_only()
    {
        var hash = _hasher.Hash("Secret123!");

        hash.ShouldNotContain("Secret123!");
        _hasher.Verify(hash, "Secret123!").ShouldBe(PasswordCheck.Success);
        _hasher.Verify(hash, "secret123!").ShouldBe(PasswordCheck.Failed);
    }

    [Fact]
    public void Same_password_hashes_differently_each_time() =>
        _hasher.Hash("Secret123!").ShouldNotBe(_hasher.Hash("Secret123!"));
}
