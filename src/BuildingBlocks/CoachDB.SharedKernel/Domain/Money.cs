using CoachDB.SharedKernel.Results;

namespace CoachDB.SharedKernel.Domain;

/// <summary>An amount in an ISO 4217 currency. Money is always <see cref="decimal"/>, never <see cref="double"/>.</summary>
public sealed record Money
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }
    public string Currency { get; }

    public static readonly Error InvalidCurrency = Error.Validation("money_invalid_currency", "Currency must be a 3-letter ISO code.");
    public static readonly Error NegativeAmount = Error.Validation("money_negative_amount", "Amount cannot be negative.");

    public static Result<Money> Create(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || !currency.All(char.IsAsciiLetter))
            return InvalidCurrency;
        if (amount < 0)
            return NegativeAmount;

        return new Money(decimal.Round(amount, 2, MidpointRounding.ToEven), currency.ToUpperInvariant());
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
