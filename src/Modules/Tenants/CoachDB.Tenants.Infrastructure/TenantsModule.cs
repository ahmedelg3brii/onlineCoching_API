using CoachDB.Application.Abstractions.Context;
using CoachDB.Infrastructure.Common;
using CoachDB.Tenants.Application;
using CoachDB.Tenants.Application.Abstractions;
using CoachDB.Tenants.Contracts;
using CoachDB.Tenants.Infrastructure.Persistence;
using CoachDB.Tenants.Infrastructure.Resolution;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoachDB.Tenants.Infrastructure;

public static class TenantsModule
{
    /// <summary>Everything the Tenants module needs except its endpoints (see <c>AddTenantsEndpoints</c>).</summary>
    public static IServiceCollection AddTenantsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<TenantsDbContext>(configuration, TenantsDbContext.SchemaName);
        services.AddOptions<TenantsOptions>().Bind(configuration.GetSection(TenantsOptions.Section));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<TenantsOptions>>().Value);

        services.AddValidatorsFromAssemblyContaining<TenantsOptions>(ServiceLifetime.Scoped, includeInternalTypes: true);

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantMemberRepository, TenantMemberRepository>();
        services.AddScoped<IPlatformPlanRepository, PlatformPlanRepository>();
        services.AddScoped<ITenantPlanSubscriptionRepository, TenantPlanSubscriptionRepository>();
        services.AddScoped<IPaymentAccountRepository, PaymentAccountRepository>();

        services.AddScoped<ITenantResolver, TenantResolver>();
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddScoped<ITenantPlanLimits, TenantPlanLimits>();

        return services;
    }
}
