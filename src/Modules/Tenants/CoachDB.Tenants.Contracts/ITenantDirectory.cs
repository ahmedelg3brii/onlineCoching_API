namespace CoachDB.Tenants.Contracts;

/// <summary>Read-only lookup of tenants (coaching businesses) for other modules, e.g. currency and time zone for Payments.</summary>
public interface ITenantDirectory
{
    Task<TenantSummary?> GetAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <param name="Status">snake_case status: <c>trial</c>, <c>active</c>, <c>past_due</c>, <c>suspended</c>, <c>cancelled</c>.</param>
public sealed record TenantSummary(
    Guid Id,
    string Slug,
    string Name,
    string Status,
    string DefaultCurrency,
    string Timezone,
    string DefaultLocale);

/// <summary>
/// The SaaS plan limits of a tenant, so modules can refuse work with <c>403 plan_limit_reached</c> or
/// <c>subscription_required</c> (e.g. Clients checks <see cref="PlanLimits.MaxClients"/> before adding one).
/// </summary>
public interface ITenantPlanLimits
{
    Task<PlanLimits?> GetAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <param name="MaxClients">null = unlimited.</param>
/// <param name="MaxCoaches">null = unlimited. Owners count as coaches.</param>
/// <param name="Features">Feature flags of the plan, e.g. <c>training</c>, <c>custom_domain</c>. Missing = off.</param>
public sealed record PlanLimits(string PlanCode, int? MaxClients, int? MaxCoaches, IReadOnlyDictionary<string, bool> Features)
{
    public bool Has(string feature) => Features.TryGetValue(feature, out var on) && on;
}
