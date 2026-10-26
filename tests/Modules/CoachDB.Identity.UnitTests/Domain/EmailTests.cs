using CoachDB.Identity.Domain;
using CoachDB.Identity.Domain.Users;

namespace CoachDB.Identity.UnitTests.Domain;

public sealed class EmailTests
{
    [Fact]
    public void Trims_and_lowercases()
    {
        var email = Email.Create("  Coach@Example.COM ");

        email.IsSuccess.ShouldBeTrue();
        email.Value.Value.ShouldBe("coach@example.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bob <bob@example.com>")]
    [InlineData("bob@localhost")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("bob@")]
    public void Rejects_invalid_input(string? input)
    {
        var email = Email.Create(input);

        email.IsFailure.ShouldBeTrue();
        email.Error.ShouldBe(IdentityErrors.InvalidEmail);
    }

    [Fact]
    public void Rejects_longer_than_255_characters()
    {
        var local = new string('a', 64);
        var host = string.Join('.', Enumerable.Repeat(new string('b', 60), 4)) + ".com"; // 248 chars
        var input = $"{local}@{host}";
        input.Length.ShouldBeGreaterThan(Email.MaxLength);

        Email.Create(input).Error.ShouldBe(IdentityErrors.InvalidEmail);
    }

    [Fact]
    public void Equal_addresses_are_equal()
    {
        Email.Create("A@b.co").Value.ShouldBe(Email.Create("a@B.CO").Value);
    }
}
