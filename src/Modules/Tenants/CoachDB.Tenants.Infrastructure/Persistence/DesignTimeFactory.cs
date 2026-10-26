using CoachDB.Application.Abstractions.Context;
using CoachDB.Infrastructure.Common;
using CoachDB.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoachDB.Tenants.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>. Migrations are applied by CoachDB.Migrator, never at app start.
/// <c>dotnet ef migrations add Name -p src/Modules/Tenants/CoachDB.Tenants.Infrastructure -o Persistence/Migrations</c>
/// </summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<TenantsDbContext>
{
    public TenantsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("COACHDB_MIGRATOR_CONNECTION")
            ?? "Host=localhost;Port=5433;Database=coachdb;Username=app_migrator;Password=app_migrator_dev";

        var options = new DbContextOptionsBuilder<TenantsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(InfrastructureSetup.MigrationsHistoryTable, TenantsDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TenantsDbContext(options, new ModuleDbContextDependencies(new TenantContext(), new DbContextScope()));
    }
}
