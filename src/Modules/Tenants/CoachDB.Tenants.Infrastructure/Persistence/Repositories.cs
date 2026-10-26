using CoachDB.Application.Abstractions.Paging;
using CoachDB.Infrastructure.Common.Persistence;
using CoachDB.Tenants.Application.Abstractions;
using CoachDB.Tenants.Application.Admin;
using CoachDB.Tenants.Domain.Billing;
using CoachDB.Tenants.Domain.Members;
using CoachDB.Tenants.Domain.Plans;
using CoachDB.Tenants.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CoachDB.Tenants.Infrastructure.Persistence;

internal sealed class TenantRepository(TenantsDbContext db) : ITenantRepository
{
    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Tenants.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
        db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug, cancellationToken);

    public async Task<IReadOnlyList<Tenant>> ListAsync(TenantStatus? status, string? search, PageRequest page, CancellationToken cancellationToken)
    {
        var query = db.Tenants.AsNoTracking();
        if (status is { } s)
            query = query.Where(t => t.Status == s);
        if (!string.IsNullOrEmpty(search))
        {
            var term = search.ToLowerInvariant();
            query = query.Where(t => t.Slug.StartsWith(term) || t.Name.ToLower().Contains(term));
        }

        if (page.After is { } after && TenantPageCursor.TryRead(after, out var createdAt, out var slug))
            query = query.Where(t => t.CreatedAt < createdAt || (t.CreatedAt == createdAt && string.Compare(t.Slug, slug) > 0));

        return await query
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Slug)
            .Take(page.Limit + 1)
            .ToListAsync(cancellationToken);
    }

    public void Add(Tenant tenant) => db.Tenants.Add(tenant);
}

internal sealed class TenantMemberRepository(TenantsDbContext db) : ITenantMemberRepository
{
    public void Add(TenantMember member) => db.Members.Add(member);

    // Cross-tenant reads of the user's own rows: the EF tenant filter is lifted, and RLS allows them through
    // app.user_id (see the tenant_members policy in the migration).
    private IQueryable<TenantMember> OwnRowsInAllTenants(Guid userId) =>
        db.Members.IgnoreQueryFilters([ModuleDbContext.TenantFilter]).Where(m => m.UserId == userId);

    public Task<int> CountOwnedByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        OwnRowsInAllTenants(userId).CountAsync(m => m.Role == MemberRole.Owner, cancellationToken);

    public async Task<IReadOnlyList<UserMembership>> ListForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await (
                from m in OwnRowsInAllTenants(userId).AsNoTracking()
                where m.Status == MemberStatus.Active
                join t in db.Tenants.AsNoTracking() on m.TenantId equals t.Id
                where t.Status != TenantStatus.Suspended && t.Status != TenantStatus.Cancelled
                orderby t.Name, t.Slug
                select new { Tenant = t, m.Role })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new UserMembership(r.Tenant, r.Role)).ToList();
    }

    public Task<int> CountActiveCoachesAsync(CancellationToken cancellationToken) =>
        db.Members.CountAsync(
            m => m.Status == MemberStatus.Active && (m.Role == MemberRole.Owner || m.Role == MemberRole.Coach),
            cancellationToken);
}

internal sealed class PlatformPlanRepository(TenantsDbContext db) : IPlatformPlanRepository
{
    public Task<PlatformPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Plans.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<PlatformPlan?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        db.Plans.SingleOrDefaultAsync(p => p.Code == code, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        db.Plans.AnyAsync(p => p.Code == code, cancellationToken);

    public async Task<IReadOnlyList<PlatformPlan>> ListAsync(bool activeOnly, CancellationToken cancellationToken) =>
        await db.Plans.AsNoTracking()
            .Where(p => !activeOnly || p.IsActive)
            .OrderBy(p => p.MonthlyPrice.Amount).ThenBy(p => p.Code)
            .ToListAsync(cancellationToken);

    public void Add(PlatformPlan plan) => db.Plans.Add(plan);
}

internal sealed class TenantPlanSubscriptionRepository(TenantsDbContext db) : ITenantPlanSubscriptionRepository
{
    public Task<TenantPlanSubscription?> GetCurrentAsync(CancellationToken cancellationToken) =>
        db.PlanSubscriptions.SingleOrDefaultAsync(
            s => s.Status == PlanSubscriptionStatus.Trialing || s.Status == PlanSubscriptionStatus.Active
                || s.Status == PlanSubscriptionStatus.PastDue,
            cancellationToken);

    public void Add(TenantPlanSubscription subscription) => db.PlanSubscriptions.Add(subscription);
}

internal sealed class PaymentAccountRepository(TenantsDbContext db) : IPaymentAccountRepository
{
    public async Task<IReadOnlyList<TenantPaymentAccount>> ListAsync(CancellationToken cancellationToken) =>
        await db.PaymentAccounts
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<TenantPaymentAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.PaymentAccounts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<bool> ExistsForProviderAsync(PaymentProvider provider, CancellationToken cancellationToken) =>
        db.PaymentAccounts.AnyAsync(a => a.Provider == provider, cancellationToken);

    public void Add(TenantPaymentAccount account) => db.PaymentAccounts.Add(account);

    public void Remove(TenantPaymentAccount account) => db.PaymentAccounts.Remove(account);
}
