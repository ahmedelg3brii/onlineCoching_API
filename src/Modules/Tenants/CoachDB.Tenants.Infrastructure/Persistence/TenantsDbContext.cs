using CoachDB.Infrastructure.Common.Persistence;
using CoachDB.Tenants.Domain.Billing;
using CoachDB.Tenants.Domain.Members;
using CoachDB.Tenants.Domain.Plans;
using CoachDB.Tenants.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace CoachDB.Tenants.Infrastructure.Persistence;

/// <summary>
/// Tenants owns the <c>tenants</c> schema: <c>tenants</c> and <c>platform_plans</c> (global), and the tenant-owned
/// <c>tenant_members</c>, <c>tenant_plan_subscriptions</c> and <c>tenant_payment_accounts</c> (RLS) (+ outbox/inbox).
/// </summary>
public sealed class TenantsDbContext(DbContextOptions<TenantsDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "tenants";

    public override string Schema => SchemaName;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PlatformPlan> Plans => Set<PlatformPlan>();
    public DbSet<TenantMember> Members => Set<TenantMember>();
    public DbSet<TenantPlanSubscription> PlanSubscriptions => Set<TenantPlanSubscription>();
    public DbSet<TenantPaymentAccount> PaymentAccounts => Set<TenantPaymentAccount>();
}
