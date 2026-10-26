using System.Text.Json;
using CoachDB.Application.Abstractions.Context;
using CoachDB.Infrastructure.Common.Persistence;
using CoachDB.SharedKernel.Results;
using CoachDB.Tenants.Application.Authorization;
using CoachDB.Tenants.Contracts;
using CoachDB.Tenants.Domain.Members;
using CoachDB.Tenants.Domain.Tenants;
using CoachDB.Tenants.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoachDB.Tenants.Infrastructure.Resolution;

/// <summary>
/// Implements <see cref="ITenantResolver"/> for the host's tenant resolution middleware: slug → tenant, then the signed-in
/// user's active membership → role → permissions. Runs before the tenant is set, so the membership lookup lifts the
/// EF tenant filter and relies on RLS letting a user read their own <c>tenant_members</c> rows.
/// Clients (customers) of a business will be resolved here too once the Clients module exists.
/// </summary>
internal sealed class TenantResolver(TenantsDbContext db) : ITenantResolver
{
    public async Task<Result<ResolvedTenant>> ResolveAsync(string slug, Guid? userId, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Slug == slug)
            .Select(t => new { t.Id, t.Slug, t.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (tenant is null)
            return TenancyErrors.TenantNotFound;
        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
            return TenancyErrors.TenantSuspended;
        if (userId is null)
            return new ResolvedTenant(tenant.Id, tenant.Slug, null, []);

        var role = await db.Members.AsNoTracking()
            .IgnoreQueryFilters([ModuleDbContext.TenantFilter])
            .Where(m => m.TenantId == tenant.Id && m.UserId == userId && m.Status == MemberStatus.Active)
            .Select(m => (MemberRole?)m.Role)
            .SingleOrDefaultAsync(cancellationToken);

        return role is { } r
            ? new ResolvedTenant(tenant.Id, tenant.Slug, JsonNamingPolicy.SnakeCaseLower.ConvertName(r.ToString()), RolePermissions.For(r))
            : TenancyErrors.TenantAccessDenied;
    }
}

/// <summary>Implements the public <see cref="ITenantDirectory"/> contract (read-only, no tracking).</summary>
internal sealed class TenantDirectory(TenantsDbContext db) : ITenantDirectory
{
    public async Task<TenantSummary?> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var t = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        return t is null
            ? null
            : new TenantSummary(t.Id, t.Slug, t.Name, JsonNamingPolicy.SnakeCaseLower.ConvertName(t.Status.ToString()),
                t.DefaultCurrency, t.Timezone, t.DefaultLocale);
    }
}

/// <summary>Implements the public <see cref="ITenantPlanLimits"/> contract from the tenant's current plan.</summary>
internal sealed class TenantPlanLimits(TenantsDbContext db) : ITenantPlanLimits
{
    public async Task<PlanLimits?> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var plan = await (
                from t in db.Tenants.AsNoTracking()
                where t.Id == tenantId
                join p in db.Plans.AsNoTracking() on t.PlanId equals (Guid?)p.Id
                select p)
            .SingleOrDefaultAsync(cancellationToken);

        return plan is null ? null : new PlanLimits(plan.Code, plan.MaxClients, plan.MaxCoaches, plan.Features);
    }
}
