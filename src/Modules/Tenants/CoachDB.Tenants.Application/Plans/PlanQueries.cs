using CoachDB.Application.Abstractions.Authorization;
using CoachDB.Application.Abstractions.Context;
using CoachDB.Application.Abstractions.Dtos;
using CoachDB.Application.Abstractions.Messaging;
using CoachDB.Application.Abstractions.Paging;
using CoachDB.SharedKernel.Results;
using CoachDB.Tenants.Application.Abstractions;
using CoachDB.Tenants.Domain.Billing;
using CoachDB.Tenants.Domain.Plans;

namespace CoachDB.Tenants.Application.Plans;

/// <summary>A SaaS plan. <see cref="Features"/> keys are snake_case flags (<c>training</c>, <c>custom_domain</c>); missing = off.</summary>
public sealed record PlanResponse(
    Guid Id,
    string Code,
    string Name,
    MoneyDto MonthlyPrice,
    int? MaxClients,
    int? MaxCoaches,
    IReadOnlyDictionary<string, bool> Features,
    bool IsActive)
{
    public static PlanResponse From(PlatformPlan plan) => new(
        plan.Id, plan.Code, plan.Name, MoneyDto.From(plan.MonthlyPrice), plan.MaxClients, plan.MaxCoaches, plan.Features, plan.IsActive);
}

/// <summary>The public price list of SaaS plans (<c>GET /plans</c>), cheapest first. Anonymous; never paged.</summary>
public sealed record ListPlansQuery : IQuery<CursorPage<PlanResponse>>;

internal sealed class ListPlansHandler(IPlatformPlanRepository plans) : IQueryHandler<ListPlansQuery, CursorPage<PlanResponse>>
{
    public async ValueTask<Result<CursorPage<PlanResponse>>> Handle(ListPlansQuery query, CancellationToken cancellationToken)
    {
        var active = await plans.ListAsync(activeOnly: true, cancellationToken);
        return new CursorPage<PlanResponse>(active.Select(PlanResponse.From).ToList(), null, false);
    }
}

/// <param name="Plan">The plan whose limits apply now; null only if the tenant has none (data repair needed).</param>
/// <param name="Subscription">The current (trialing, active or past-due) subscription, if any.</param>
public sealed record TenantPlanResponse(PlanResponse? Plan, PlanSubscriptionResponse? Subscription, PlanUsageResponse Usage);

public sealed record PlanSubscriptionResponse(
    Guid Id,
    PlanSubscriptionStatus Status,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset CurrentPeriodEnd,
    bool CancelAtPeriodEnd)
{
    public static PlanSubscriptionResponse From(TenantPlanSubscription s) =>
        new(s.Id, s.Status, s.CurrentPeriodStart, s.CurrentPeriodEnd, s.CancelAtPeriodEnd);
}

/// <summary>Usage against the plan's limits. Client counts are added when the Clients module exists.</summary>
public sealed record PlanUsageResponse(int Coaches, int? MaxCoaches, int? MaxClients);

/// <summary>
/// The current business's SaaS plan, subscription and usage (<c>GET /tenant/plan</c>). Any member may read it, so the
/// app can show or hide features the plan doesn't include.
/// </summary>
[RequiresTenant]
public sealed record GetTenantPlanQuery : IQuery<TenantPlanResponse>;

internal sealed class GetTenantPlanHandler(
    ITenantContext tenant,
    ITenantRepository tenants,
    IPlatformPlanRepository plans,
    ITenantPlanSubscriptionRepository subscriptions,
    ITenantMemberRepository members) : IQueryHandler<GetTenantPlanQuery, TenantPlanResponse>
{
    public async ValueTask<Result<TenantPlanResponse>> Handle(GetTenantPlanQuery query, CancellationToken cancellationToken)
    {
        var current = await tenants.GetByIdAsync(tenant.TenantId, cancellationToken);
        var plan = current?.PlanId is { } planId ? await plans.GetByIdAsync(planId, cancellationToken) : null;
        var subscription = await subscriptions.GetCurrentAsync(cancellationToken);
        var coaches = await members.CountActiveCoachesAsync(cancellationToken);

        return new TenantPlanResponse(
            plan is null ? null : PlanResponse.From(plan),
            subscription is null ? null : PlanSubscriptionResponse.From(subscription),
            new PlanUsageResponse(coaches, plan?.MaxCoaches, plan?.MaxClients));
    }
}
