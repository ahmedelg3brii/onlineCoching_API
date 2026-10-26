using CoachDB.Application.Abstractions.Paging;
using CoachDB.Tenants.Domain.Billing;
using CoachDB.Tenants.Domain.Members;
using CoachDB.Tenants.Domain.Plans;
using CoachDB.Tenants.Domain.Tenants;

namespace CoachDB.Tenants.Application.Abstractions;

// What the use cases need from the outside world. Implemented in CoachDB.Tenants.Infrastructure.
// Repositories of tenant-owned entities only ever see the current tenant (EF filter + RLS),
// except where a method says it reads the user's own rows across tenants.

public interface ITenantRepository
{
    /// <summary>Tracked, for changes.</summary>
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Read-only.</summary>
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Includes deleted tenants: a slug is never reused.</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Platform admin list, newest first. Returns up to <c>page.Limit + 1</c> rows (see <see cref="CursorPage{T}.From{TRow}"/>).</summary>
    Task<IReadOnlyList<Tenant>> ListAsync(TenantStatus? status, string? search, PageRequest page, CancellationToken cancellationToken);

    void Add(Tenant tenant);
}

public interface ITenantMemberRepository
{
    void Add(TenantMember member);

    /// <summary>Across tenants: how many businesses <paramref name="userId"/> owns.</summary>
    Task<int> CountOwnedByUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Across tenants: the user's active memberships of accessible businesses, by business name.</summary>
    Task<IReadOnlyList<UserMembership>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Current tenant: active owners and coaches (they count towards the plan's coach limit).</summary>
    Task<int> CountActiveCoachesAsync(CancellationToken cancellationToken);
}

public sealed record UserMembership(Tenant Tenant, MemberRole Role);

public interface IPlatformPlanRepository
{
    Task<PlatformPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<PlatformPlan?> GetByCodeAsync(string code, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>Cheapest first.</summary>
    Task<IReadOnlyList<PlatformPlan>> ListAsync(bool activeOnly, CancellationToken cancellationToken);

    void Add(PlatformPlan plan);
}

public interface ITenantPlanSubscriptionRepository
{
    /// <summary>Current tenant: the trialing, active or past-due subscription, if any.</summary>
    Task<TenantPlanSubscription?> GetCurrentAsync(CancellationToken cancellationToken);

    void Add(TenantPlanSubscription subscription);
}

public interface IPaymentAccountRepository
{
    /// <summary>Current tenant, default first, tracked (a handful of rows).</summary>
    Task<IReadOnlyList<TenantPaymentAccount>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Current tenant, tracked. Another tenant's id is simply not found.</summary>
    Task<TenantPaymentAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsForProviderAsync(PaymentProvider provider, CancellationToken cancellationToken);

    void Add(TenantPaymentAccount account);
    void Remove(TenantPaymentAccount account);
}
