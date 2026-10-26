using CoachDB.Application.Abstractions.Authorization;
using CoachDB.Application.Abstractions.Context;
using CoachDB.Application.Abstractions.Messaging;
using CoachDB.Application.Abstractions.Paging;
using CoachDB.SharedKernel.Results;
using CoachDB.Tenants.Application.Abstractions;
using CoachDB.Tenants.Domain;
using CoachDB.Tenants.Domain.Billing;
using FluentValidation;

namespace CoachDB.Tenants.Application.PaymentAccounts;

// The owner's payout accounts (/tenant/payment-accounts): where clients' payments go. Owner only (tenant.billing).

public sealed record PaymentAccountResponse(
    Guid Id,
    PaymentProvider Provider,
    string? ProviderAccountId,
    PaymentAccountStatus Status,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static PaymentAccountResponse From(TenantPaymentAccount a) =>
        new(a.Id, a.Provider, a.ProviderAccountId, a.Status, a.IsDefault, a.CreatedAt, a.UpdatedAt);
}

[RequiresPermission(Permissions.Tenant.Billing)]
public sealed record ListPaymentAccountsQuery : IQuery<CursorPage<PaymentAccountResponse>>;

internal sealed class ListPaymentAccountsHandler(IPaymentAccountRepository accounts)
    : IQueryHandler<ListPaymentAccountsQuery, CursorPage<PaymentAccountResponse>>
{
    public async ValueTask<Result<CursorPage<PaymentAccountResponse>>> Handle(ListPaymentAccountsQuery query, CancellationToken cancellationToken)
    {
        var all = await accounts.ListAsync(cancellationToken);
        return new CursorPage<PaymentAccountResponse>(all.Select(PaymentAccountResponse.From).ToList(), null, false);
    }
}

[RequiresPermission(Permissions.Tenant.Billing)]
public sealed record GetPaymentAccountQuery(Guid AccountId) : IQuery<PaymentAccountResponse>;

internal sealed class GetPaymentAccountHandler(IPaymentAccountRepository accounts) : IQueryHandler<GetPaymentAccountQuery, PaymentAccountResponse>
{
    public async ValueTask<Result<PaymentAccountResponse>> Handle(GetPaymentAccountQuery query, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(query.AccountId, cancellationToken);
        return account is null ? TenantsErrors.PaymentAccountNotFound : PaymentAccountResponse.From(account);
    }
}

/// <summary>
/// Saves a payout account (status <c>pending</c> until the Payments module verifies it with the provider). One per provider.
/// The first account becomes the default; <see cref="MakeDefault"/> moves the default to this one.
/// </summary>
[RequiresPermission(Permissions.Tenant.Billing)]
public sealed record ConnectPaymentAccountCommand(PaymentProvider Provider, string? ProviderAccountId, bool MakeDefault)
    : ICommand<PaymentAccountResponse>;

internal sealed class ConnectPaymentAccountValidator : AbstractValidator<ConnectPaymentAccountCommand>
{
    public ConnectPaymentAccountValidator()
    {
        RuleFor(x => x.Provider).IsInEnum().WithMessage("unsupported_provider");
        RuleFor(x => x.ProviderAccountId).MaximumLength(TenantPaymentAccount.MaxProviderAccountIdLength).WithMessage("too_long");
    }
}

internal sealed class ConnectPaymentAccountHandler(ITenantContext tenant, IPaymentAccountRepository accounts, TimeProvider clock)
    : ICommandHandler<ConnectPaymentAccountCommand, PaymentAccountResponse>
{
    public async ValueTask<Result<PaymentAccountResponse>> Handle(ConnectPaymentAccountCommand command, CancellationToken cancellationToken)
    {
        if (await accounts.ExistsForProviderAsync(command.Provider, cancellationToken))
            return TenantsErrors.PaymentAccountExists;

        var now = clock.GetUtcNow();
        var existing = await accounts.ListAsync(cancellationToken);
        var isDefault = command.MakeDefault || existing.Count == 0;
        if (isDefault)
        {
            foreach (var other in existing)
                other.ClearDefault(now);
        }

        var account = TenantPaymentAccount.Connect(tenant.TenantId, command.Provider, command.ProviderAccountId, isDefault, now);
        accounts.Add(account);
        return PaymentAccountResponse.From(account);
    }
}

[RequiresPermission(Permissions.Tenant.Billing)]
public sealed record MakeDefaultPaymentAccountCommand(Guid AccountId) : ICommand<PaymentAccountResponse>;

internal sealed class MakeDefaultPaymentAccountHandler(IPaymentAccountRepository accounts, TimeProvider clock)
    : ICommandHandler<MakeDefaultPaymentAccountCommand, PaymentAccountResponse>
{
    public async ValueTask<Result<PaymentAccountResponse>> Handle(MakeDefaultPaymentAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken);
        if (account is null)
            return TenantsErrors.PaymentAccountNotFound;

        var now = clock.GetUtcNow();
        var made = account.MakeDefault(now);
        if (made.IsFailure)
            return made.Error;

        foreach (var other in await accounts.ListAsync(cancellationToken))
        {
            if (other.Id != account.Id)
                other.ClearDefault(now);
        }

        return PaymentAccountResponse.From(account);
    }
}

[RequiresPermission(Permissions.Tenant.Billing)]
public sealed record RemovePaymentAccountCommand(Guid AccountId) : ICommand;

internal sealed class RemovePaymentAccountHandler(IPaymentAccountRepository accounts) : ICommandHandler<RemovePaymentAccountCommand>
{
    public async ValueTask<Result> Handle(RemovePaymentAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken);
        if (account is null)
            return TenantsErrors.PaymentAccountNotFound;

        accounts.Remove(account);
        return Result.Success();
    }
}
