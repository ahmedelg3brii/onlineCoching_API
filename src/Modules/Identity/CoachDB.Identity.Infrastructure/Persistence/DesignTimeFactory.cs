using CoachDB.Application.Abstractions.Context;
using CoachDB.Infrastructure.Common;
using CoachDB.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoachDB.Identity.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>. Migrations are applied by CoachDB.Migrator, never at app start.
/// <c>dotnet ef migrations add Name -p src/Modules/Identity/CoachDB.Identity.Infrastructure -o Persistence/Migrations</c>
/// </summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("COACHDB_MIGRATOR_CONNECTION")
            ?? "Host=localhost;Port=5433;Database=coachdb;Username=app_migrator;Password=app_migrator_dev";

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(InfrastructureSetup.MigrationsHistoryTable, IdentityDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IdentityDbContext(options, new ModuleDbContextDependencies(new TenantContext(), new DbContextScope()));
    }
}
