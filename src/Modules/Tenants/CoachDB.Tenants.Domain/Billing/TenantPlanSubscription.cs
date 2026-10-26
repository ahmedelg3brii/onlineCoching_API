using CoachDB.SharedKernel.Domain;

namespace CoachDB.Tenants.Domain.Billing;

public enum PlanSubscriptionStatus
{
    Trialing,
    Active,
    PastDue,
    Cancelled,
    Expired,
}

/// <summary>Payment providers the platform integrates with (Paymob first; Fawry and Stripe in Phase 2).</summary>
public enum PaymentProvider
{
    Paymob,
    Fawry,
    Stripe,
}

/// <summary>
/// A business's subscription to a SaaS plan (<c>tenant_plan_subscriptions</c>): the trial now; paid periods once
/// platform billing (<c>POST /tenant/plan/checkout</c>) is built on the Payments module.
/// At most one subscription per tenant is current (trialing, active or past due).
/// </summary>
public sealed class TenantPlanSubscription : Entity<Guid>, ITenantOwned, IHasTimestamps
{
    private TenantPlanSubscription() { } // EF Core

    private TenantPlanSubscription(Guid tenantId, Guid planId, PlanSubscriptionStatus status,
        DateTimeOffset periodStart, DateTimeOffset periodEnd, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        TenantId = tenantId;
        PlanId = planId;
        Status = status;
        CurrentPeriodStart = periodStart;
        CurrentPeriodEnd = periodEnd;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid TenantId { get; private set; }
    public Guid PlanId { get; private set; }
    public PlanSubscriptionStatus Status { get; private set; }
    public DateTimeOffset CurrentPeriodStart { get; private set; }
    public DateTimeOffset CurrentPeriodEnd { get; private set; }
    public PaymentProvider? Provider { get; private set; }
    public string? ProviderSubscriptionId { get; private set; }
    public bool CancelAtPeriodEnd { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static readonly PlanSubscriptionStatus[] CurrentStatuses =
        [PlanSubscriptionStatus.Trialing, PlanSubscriptionStatus.Active, PlanSubscriptionStatus.PastDue];

    public bool IsCurrent => CurrentStatuses.Contains(Status);

    public static TenantPlanSubscription StartTrial(Guid tenantId, Guid planId, DateTimeOffset now, DateTimeOffset trialEndsAt) =>
        new(tenantId, planId, PlanSubscriptionStatus.Trialing, now, trialEndsAt, now);
}
