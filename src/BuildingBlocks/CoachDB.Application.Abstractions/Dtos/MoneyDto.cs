using CoachDB.SharedKernel.Domain;

namespace CoachDB.Application.Abstractions.Dtos;

/// <summary>Money on the wire: <c>{ "amount": 1500.00, "currency": "EGP" }</c>.</summary>
public sealed record MoneyDto(decimal Amount, string Currency)
{
    public static MoneyDto From(Money money) => new(money.Amount, money.Currency);
}
