using CoachDB.SharedKernel.Domain;

namespace CoachDB.BuildingBlocks.UnitTests.Domain;

public sealed class MoneyTests
{
    [Fact]
    public void Creates_valid_money()
    {
        var result = Money.Create(1500m, "EGP");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(1500m);
        result.Value.Currency.ShouldBe("EGP");
    }

    [Fact]
    public void Zero_is_allowed()
    {
        Money.Create(0m, "USD").IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EG")]
    [InlineData("EGPP")]
    [InlineData("E1P")]
    public void Rejects_invalid_currency(string currency)
    {
        var result = Money.Create(10m, currency);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Money.InvalidCurrency);
    }

    [Fact]
    public void Rejects_negative_amount()
    {
        var result = Money.Create(-0.01m, "EGP");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Money.NegativeAmount);
    }

    [Theory]
    [InlineData("10.125", "10.12")] // banker's rounding: to even
    [InlineData("10.135", "10.14")]
    [InlineData("10.129", "10.13")]
    public void Rounds_to_two_decimals_half_to_even(string input, string expected)
    {
        Money.Create(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture), "EGP")
            .Value.Amount.ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Upper_cases_the_currency()
    {
        Money.Create(5m, "egp").Value.Currency.ShouldBe("EGP");
    }

    [Fact]
    public void Equal_amounts_and_currencies_are_equal()
    {
        Money.Create(5m, "EGP").Value.ShouldBe(Money.Create(5.00m, "egp").Value);
    }
}
