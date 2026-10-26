using CoachDB.Infrastructure.Common.Persistence;
using CoachDB.Tenants.Domain.Billing;
using CoachDB.Tenants.Domain.Members;
using CoachDB.Tenants.Domain.Plans;
using CoachDB.Tenants.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoachDB.Tenants.Infrastructure.Persistence;

// Column names come from the snake_case convention; types and defaults mirror db/reference/schema_import.sql.
// Enums are stored as snake_case text with a CHECK constraint listing the allowed values.

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", t =>
        {
            t.HasCheckConstraint("ck_tenants_status", $"status IN ({PropertyBuilderExtensions.SqlValues<TenantStatus>()})");
            t.HasCheckConstraint("ck_tenants_slug", "slug ~ '^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])$' AND slug NOT LIKE '%--%'");
            t.HasCheckConstraint("ck_tenants_default_currency", "default_currency ~ '^[A-Z]{3}$'");
        });
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Name).IsRequired();
        builder.Property(t => t.Slug).HasColumnType("varchar(255)").IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Property(t => t.Status).HasSnakeCaseConversion().HasSnakeCaseDefault(TenantStatus.Trial);
        builder.Property(t => t.DefaultCurrency).HasColumnType("char(3)").HasDefaultValueSql("'EGP'");
        builder.Property(t => t.Timezone).IsRequired().HasDefaultValueSql("'Africa/Cairo'");
        builder.Property(t => t.DefaultLocale).IsRequired().HasDefaultValueSql("'ar'");
        builder.Property(t => t.Branding).HasJsonbConversion();
        builder.Property(t => t.Settings).HasJsonbConversion();
        builder.Property(t => t.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(t => t.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(t => t.CreatedAt); // admin list, newest first
        builder.HasOne<PlatformPlan>().WithMany().HasForeignKey(t => t.PlanId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(t => t.DomainEvents);
        builder.Ignore(t => t.IsAccessible);
    }
}

internal sealed class PlatformPlanConfiguration : IEntityTypeConfiguration<PlatformPlan>
{
    public void Configure(EntityTypeBuilder<PlatformPlan> builder)
    {
        builder.ToTable("platform_plans", t =>
        {
            t.HasCheckConstraint("ck_platform_plans_monthly_price", "monthly_price >= 0");
            t.HasCheckConstraint("ck_platform_plans_limits", "(max_clients IS NULL OR max_clients > 0) AND (max_coaches IS NULL OR max_coaches > 0)");
        });
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Code).IsRequired();
        builder.HasIndex(p => p.Code).IsUnique();
        builder.Property(p => p.Name).IsRequired();
        builder.ComplexProperty(p => p.MonthlyPrice, money =>
        {
            money.Property(m => m.Amount).HasColumnName("monthly_price").HasPrecision(12, 2);
            money.Property(m => m.Currency).HasColumnName("currency").HasColumnType("char(3)").HasDefaultValueSql("'USD'");
        });
        builder.Property(p => p.Features).HasJsonbConversion();
        builder.Property(p => p.IsActive).HasDefaultValue(true).HasSentinel(true); // false is still written on insert
        builder.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");

        builder.Ignore(p => p.DomainEvents);
    }
}

internal sealed class TenantMemberConfiguration : IEntityTypeConfiguration<TenantMember>
{
    public void Configure(EntityTypeBuilder<TenantMember> builder)
    {
        builder.ToTable("tenant_members", t =>
        {
            t.HasCheckConstraint("ck_tenant_members_role", $"role IN ({PropertyBuilderExtensions.SqlValues<MemberRole>()})");
            t.HasCheckConstraint("ck_tenant_members_status", $"status IN ({PropertyBuilderExtensions.SqlValues<MemberStatus>()})");
        });
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Role).HasSnakeCaseConversion();
        builder.Property(m => m.Status).HasSnakeCaseConversion().HasSnakeCaseDefault(MemberStatus.Active);
        builder.Property(m => m.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(m => m.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId); // "my businesses" and tenant resolution by user
        builder.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Cascade);
        // user_id → identity.users is a cross-module FK, added in SQL by the migration.

        builder.Ignore(m => m.IsActive);
        builder.Ignore(m => m.CountsAsCoach);
    }
}

internal sealed class TenantPlanSubscriptionConfiguration : IEntityTypeConfiguration<TenantPlanSubscription>
{
    public void Configure(EntityTypeBuilder<TenantPlanSubscription> builder)
    {
        builder.ToTable("tenant_plan_subscriptions", t =>
        {
            t.HasCheckConstraint("ck_tenant_plan_subscriptions_status", $"status IN ({PropertyBuilderExtensions.SqlValues<PlanSubscriptionStatus>()})");
            t.HasCheckConstraint("ck_tenant_plan_subscriptions_provider", $"provider IS NULL OR provider IN ({PropertyBuilderExtensions.SqlValues<PaymentProvider>()})");
            t.HasCheckConstraint("ck_tenant_plan_subscriptions_period", "current_period_end > current_period_start");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Status).HasSnakeCaseConversion();
        builder.Property(s => s.Provider).HasSnakeCaseConversion();

        builder.Property(s => s.CancelAtPeriodEnd).HasDefaultValue(false);
        builder.Property(s => s.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(s => s.UpdatedAt).HasDefaultValueSql("now()");

        // At most one current subscription per tenant.
        builder.HasIndex(s => s.TenantId).IsUnique()
            .HasFilter("status IN ('trialing', 'active', 'past_due')")
            .HasDatabaseName("ix_tenant_plan_subscriptions_current");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PlatformPlan>().WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(s => s.IsCurrent);
    }
}

internal sealed class TenantPaymentAccountConfiguration : IEntityTypeConfiguration<TenantPaymentAccount>
{
    public void Configure(EntityTypeBuilder<TenantPaymentAccount> builder)
    {
        builder.ToTable("tenant_payment_accounts", t =>
        {
            t.HasCheckConstraint("ck_tenant_payment_accounts_provider", $"provider IN ({PropertyBuilderExtensions.SqlValues<PaymentProvider>()})");
            t.HasCheckConstraint("ck_tenant_payment_accounts_status", $"status IN ({PropertyBuilderExtensions.SqlValues<PaymentAccountStatus>()})");
        });
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Provider).HasSnakeCaseConversion();
        builder.Property(a => a.Status).HasSnakeCaseConversion().HasSnakeCaseDefault(PaymentAccountStatus.Pending);
        builder.Property(a => a.IsDefault).HasDefaultValue(false);
        builder.Property(a => a.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(a => a.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(a => new { a.TenantId, a.Provider }).IsUnique();
        // "At most one default per tenant" is a deferred exclusion constraint added in SQL by the migration,
        // so moving the default between two accounts in one save never trips it mid-statement.
        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
