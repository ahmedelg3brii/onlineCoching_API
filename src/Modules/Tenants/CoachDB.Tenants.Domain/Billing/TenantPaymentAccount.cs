using CoachDB.SharedKernel.Domain;
using CoachDB.SharedKernel.Results;

namespace CoachDB.Tenants.Domain.Billing;

public enum PaymentAccountStatus
{
    /// <summary>Saved by the owner, not yet verified with the provider (verification comes with the Payments module).</summary>
    Pending,
    Active,
    Disabled,
}

/// <summary>
/// Where a business gets paid by its clients (<c>tenant_payment_accounts</c>): one account per provider, at most one
/// default. No secrets are stored here; <see cref="ProviderAccountId"/> is the provider's public merchant/account id.
/// </summary>
public sealed class TenantPaymentAccount : Entity<Guid>, ITenantOwned, IHasTimestamps
{
    public const int MaxProviderAccountIdLength = 200;

    private TenantPaymentAccount() { } // EF Core

    private TenantPaymentAccount(Guid tenantId, PaymentProvider provider, string? providerAccountId, bool isDefault, DateTimeOffset now)
        : base(Guid.CreateVersion7())
    {
        TenantId = tenantId;
        Provider = provider;
        ProviderAccountId = providerAccountId;
        Status = PaymentAccountStatus.Pending;
        IsDefault = isDefault;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid TenantId { get; private set; }
    public PaymentProvider Provider { get; private set; }
    public string? ProviderAccountId { get; private set; }
    public PaymentAccountStatus Status { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TenantPaymentAccount Connect(
        Guid tenantId, PaymentProvider provider, string? providerAccountId, bool isDefault, DateTimeOffset now) =>
        new(tenantId, provider, string.IsNullOrWhiteSpace(providerAccountId) ? null : providerAccountId.Trim(), isDefault, now);

    /// <summary>The caller clears the previous default in the same unit of work.</summary>
    public Result MakeDefault(DateTimeOffset now)
    {
        if (Status == PaymentAccountStatus.Disabled)
            return TenantsErrors.PaymentAccountDisabled;

        IsDefault = true;
        UpdatedAt = now;
        return Result.Success();
    }

    public void ClearDefault(DateTimeOffset now)
    {
        if (!IsDefault)
            return;

        IsDefault = false;
        UpdatedAt = now;
    }
}
