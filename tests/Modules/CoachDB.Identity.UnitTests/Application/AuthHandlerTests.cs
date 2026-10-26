using CoachDB.Identity.Application.Auth.Login;
using CoachDB.Identity.Application.Auth.Logout;
using CoachDB.Identity.Application.Auth.Refresh;
using CoachDB.Identity.Application.Auth.Register;
using CoachDB.Identity.Domain;
using CoachDB.Identity.Domain.Sessions;

namespace CoachDB.Identity.UnitTests.Application;

public sealed class RegisterHandlerTests
{
    private readonly IdentityFixture _f = new();

    private RegisterHandler Handler => new(_f.Users, _f.Hasher, _f.Sessions, _f.Clock);

    [Fact]
    public async Task Fails_with_email_taken_when_the_email_exists()
    {
        _f.AddUser("coach@example.com");

        var result = await Handler.Handle(new RegisterCommand("COACH@example.com", "Secret123!", "X", null), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(IdentityErrors.EmailTaken);
        _f.Users.Users.Count.ShouldBe(1);
        _f.Tokens.Tokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fails_with_email_invalid_for_a_bad_email()
    {
        var result = await Handler.Handle(new RegisterCommand("Bob <bob@example.com>", "Secret123!", "Bob", null), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(IdentityErrors.InvalidEmail);
        _f.Users.Users.ShouldBeEmpty();
    }

    [Fact]
    public async Task Creates_the_user_and_starts_a_session()
    {
        var result = await Handler.Handle(new RegisterCommand(" New@Example.com ", "Secret123!", "New Coach", "en"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var user = _f.Users.Users.ShouldHaveSingleItem();
        user.Email.Value.ShouldBe("new@example.com");
        user.PasswordHash.ShouldNotBe("Secret123!");
        user.DomainEvents.Count.ShouldBe(1);

        var stored = _f.Tokens.Tokens.ShouldHaveSingleItem();
        stored.UserId.ShouldBe(user.Id);
        stored.TokenHash.ShouldBe(_f.Generator.Hash(result.Value.RefreshToken));

        result.Value.User.Email.ShouldBe("new@example.com");
        result.Value.User.CreatedAt.ShouldBe(IdentityFixture.Start);
        result.Value.AccessToken.ShouldNotBeNullOrEmpty();
        result.Value.AccessTokenExpiresAt.ShouldBe(IdentityFixture.Start.AddMinutes(15));
        result.Value.RefreshTokenExpiresAt.ShouldBe(IdentityFixture.Start.AddDays(30));
    }
}

public sealed class LoginHandlerTests
{
    private readonly IdentityFixture _f = new();

    private LoginHandler Handler => new(_f.Users, _f.Hasher, _f.Sessions, _f.Clock);

    [Theory]
    [InlineData("nobody@example.com", "Secret123!")]   // unknown email
    [InlineData("coach@example.com", "wrong-password")] // wrong password
    [InlineData("not-an-email", "Secret123!")]          // unparsable email
    public async Task Fails_with_the_same_invalid_credentials_error(string email, string password)
    {
        _f.AddUser("coach@example.com", "Secret123!");

        var result = await Handler.Handle(new LoginCommand(email, password), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(IdentityErrors.InvalidCredentials);
        _f.Tokens.Tokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fails_for_an_account_without_a_password()
    {
        var user = _f.AddUser("google@example.com");
        typeof(Identity.Domain.Users.User).GetProperty(nameof(user.PasswordHash))!.SetValue(user, null);

        var result = await Handler.Handle(new LoginCommand("google@example.com", "anything"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(IdentityErrors.InvalidCredentials);
    }

    [Fact]
    public async Task Succeeds_records_login_and_starts_a_session()
    {
        var user = _f.AddUser("coach@example.com", "Secret123!");
        _f.Clock.Now = IdentityFixture.Start.AddHours(2);

        var result = await Handler.Handle(new LoginCommand("Coach@Example.com", "Secret123!"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        user.LastLoginAt.ShouldBe(_f.Clock.Now);
        _f.Tokens.Tokens.ShouldHaveSingleItem().UserId.ShouldBe(user.Id);
        result.Value.User.Id.ShouldBe(user.Id.Value);
    }
}

public sealed class RefreshSessionHandlerTests
{
    private readonly IdentityFixture _f = new();

    private RefreshSessionHandler Handler => new(_f.Tokens, _f.Generator, _f.Users, _f.Sessions, _f.Clock);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown-token")]
    public async Task Missing_or_unknown_token_is_invalid(string? token)
    {
        var result = await Handler.Handle(new RefreshSessionCommand(token), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(IdentityErrors.RefreshTokenInvalid);
    }

    [Fact]
    public async Task Expired_token_is_invalid()
    {
        var (plain, stored) = _f.StartSession(_f.AddUser());
        _f.Clock.Now = stored.ExpiresAt;

        var result = await Handler.Handle(new RefreshSessionCommand(plain), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(IdentityErrors.RefreshTokenInvalid);
    }

    [Fact]
    public async Task Active_token_is_rotated()
    {
        var user = _f.AddUser();
        var (plain, stored) = _f.StartSession(user);
        _f.Clock.Now = IdentityFixture.Start.AddMinutes(20);

        var result = await Handler.Handle(new RefreshSessionCommand(plain), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RefreshToken.ShouldNotBe(plain);
        stored.RevokedReason.ShouldBe(RefreshToken.RotatedReason);
        stored.RevokedAt.ShouldBe(_f.Clock.Now);

        var next = _f.Tokens.Tokens.Last();
        next.FamilyId.ShouldBe(stored.FamilyId);
        next.TokenHash.ShouldBe(_f.Generator.Hash(result.Value.RefreshToken));
        stored.ReplacedByTokenId.ShouldBe(next.Id);
        _f.Tokens.RevokedFamilies.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_whole_family()
    {
        var (plain, stored) = _f.StartSession(_f.AddUser());
        var rotated = await Handler.Handle(new RefreshSessionCommand(plain), TestContext.Current.CancellationToken);
        rotated.IsSuccess.ShouldBeTrue();

        var reuse = await Handler.Handle(new RefreshSessionCommand(plain), TestContext.Current.CancellationToken);

        reuse.Error.ShouldBe(IdentityErrors.RefreshTokenReused);
        _f.Tokens.RevokedFamilies.ShouldHaveSingleItem().ShouldBe((stored.FamilyId, RefreshToken.ReuseDetectedReason));

        // The successor issued to whoever rotated first is now dead too, and reports "invalid", not "reused".
        var successor = await Handler.Handle(new RefreshSessionCommand(rotated.Value.RefreshToken), TestContext.Current.CancellationToken);
        successor.Error.ShouldBe(IdentityErrors.RefreshTokenInvalid);
    }

    [Fact]
    public async Task Token_revoked_by_logout_is_invalid_not_reused()
    {
        var (plain, stored) = _f.StartSession(_f.AddUser());
        FakeRefreshTokenRepository.ForceRevoke(stored, RefreshToken.LogoutReason, _f.Clock.Now);

        var result = await Handler.Handle(new RefreshSessionCommand(plain), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(IdentityErrors.RefreshTokenInvalid);
        _f.Tokens.RevokedFamilies.ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_of_a_missing_user_is_invalid()
    {
        var user = _f.AddUser();
        var (plain, _) = _f.StartSession(user);
        _f.Users.Users.Remove(user);

        var result = await Handler.Handle(new RefreshSessionCommand(plain), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(IdentityErrors.RefreshTokenInvalid);
    }
}

public sealed class LogoutHandlerTests
{
    private readonly IdentityFixture _f = new();

    private LogoutHandler Handler => new(_f.Tokens, _f.Generator, _f.Clock);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-token")]
    public async Task Always_succeeds(string? token)
    {
        var result = await Handler.Handle(new LogoutCommand(token), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _f.Tokens.RevokedFamilies.ShouldBeEmpty();
    }

    [Fact]
    public async Task Revokes_the_session_family_with_reason_logout()
    {
        var (plain, stored) = _f.StartSession(_f.AddUser());

        var result = await Handler.Handle(new LogoutCommand(plain), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _f.Tokens.RevokedFamilies.ShouldHaveSingleItem().ShouldBe((stored.FamilyId, RefreshToken.LogoutReason));
        stored.RevokedReason.ShouldBe(RefreshToken.LogoutReason);
        stored.WasRotated.ShouldBeFalse();
    }
}
