using CoachDB.Application.Abstractions.Context;
using CoachDB.Infrastructure.Common.Outbox;
using CoachDB.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoachDB.Infrastructure.Common;

public static class InfrastructureSetup
{
    public const string ConnectionStringName = "CoachDb";
    public const string MigrationsHistoryTable = "__ef_migrations";

    /// <summary>Shared persistence services. Called once by the host.</summary>
    public static IServiceCollection AddCoachDbInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddScoped<DbContextScope>();
        services.TryAddScoped<ModuleDbContextDependencies>();
        services.TryAddScoped<TenantRlsConnectionInterceptor>();
        services.TryAddScoped<TenantStampingInterceptor>();
        services.TryAddScoped<TimestampsInterceptor>();
        GetOrAddRegistry(services);
        services.AddHostedService<OutboxDispatcher>();
        return services;
    }

    /// <summary>
    /// Registers a module's DbContext: Npgsql, snake_case names, migrations history in the module's own schema,
    /// the tenant/timestamp interceptors, and its outbox in the dispatcher.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : ModuleDbContext
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

        services.AddDbContext<TContext>((sp, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                sp.GetRequiredService<TenantRlsConnectionInterceptor>(),
                sp.GetRequiredService<TenantStampingInterceptor>(),
                sp.GetRequiredService<TimestampsInterceptor>()));

        GetOrAddRegistry(services).Add(typeof(TContext));
        return services;
    }

    // One registry instance, created at registration time so modules can add to it before the container is built.
    private static OutboxRegistry GetOrAddRegistry(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(OutboxRegistry))?.ImplementationInstance is OutboxRegistry existing)
            return existing;

        var registry = new OutboxRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}
