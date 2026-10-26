using CoachDB.Identity.Contracts.Events;
using CoachDB.Identity.Domain.Users;

namespace CoachDB.Identity.UnitTests.Domain;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = IdentityFixture.Start;
    private static readonly Email Address = Email.Create("coach@example.com").Value;

    [Theory]
    [InlineData(null, "ar")]
    [InlineData("fr", "ar")]
    [InlineData("en", "en")]
    [InlineData("ar", "ar")]
    public void Register_falls_back_to_arabic_for_unsupported_locales(string? locale, string expected)
    {
        User.Register(Address, "Ahmed", locale, "hash", Now).Locale.ShouldBe(expected);
    }

    [Fact]
    public void Register_sets_fields_and_timestamps()
    {
        var user = User.Register(Address, "  Ahmed Coach  ", "en", "hash", Now);

        user.Id.Value.ShouldNotBe(Guid.Empty);
        user.Email.ShouldBe(Address);
        user.FullName.ShouldBe("Ahmed Coach");
        user.PasswordHash.ShouldBe("hash");
        user.CreatedAt.ShouldBe(Now);
        user.UpdatedAt.ShouldBe(Now);
        user.IsPlatformAdmin.ShouldBeFalse();
        user.EmailVerifiedAt.ShouldBeNull();
        user.LastLoginAt.ShouldBeNull();
    }

    [Fact]
    public void Register_raises_exactly_one_UserRegistered()
    {
        var user = User.Register(Address, "Ahmed", "en", "hash", Now);

        var registered = user.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<UserRegistered>();
        registered.UserId.ShouldBe(user.Id.Value);
        registered.Email.ShouldBe("coach@example.com");
        registered.FullName.ShouldBe("Ahmed");
        registered.Locale.ShouldBe("en");
    }

    [Fact]
    public void RecordLogin_and_UpgradePasswordHash_update_state()
    {
        var user = User.Register(Address, "Ahmed", null, "old", Now);

        user.RecordLogin(Now.AddHours(1));
        user.UpgradePasswordHash("new");

        user.LastLoginAt.ShouldBe(Now.AddHours(1));
        user.PasswordHash.ShouldBe("new");
    }
}
