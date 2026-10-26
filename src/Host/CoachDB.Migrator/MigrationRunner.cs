using CoachDB.Identity.Infrastructure;
using CoachDB.Identity.Infrastructure.Persistence;
using CoachDB.Infrastructure.Common;
using CoachDB.Infrastructure.Common.Persistence;
using CoachDB.Tenants.Infrastructure;
using CoachDB.Tenants.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CoachDB.Migrator;

/// <summary>
/// Applies every module's EF Core migrations in dependency order, then grants the runtime roles access
/// to each module schema. Run with the <c>app_migrator</c> role, as a deploy step — never at API start.
/// Also used by integration tests to build a fresh database.
/// </summary>
public static class MigrationRunner
{
    /// <summary>Module contexts in dependency order. Add each new module here.</summary>
    private static readonly Type[] Modules =
    [
        typeof(IdentityDbContext),
        typeof(TenantsDbContext),
    ];

    public static async Task RunAsync(string connectionString, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{InfrastructureSetup.ConnectionStringName}"] = connectionString,
            })
            .Build();

        var services = new ServiceCollection()
            .AddSingleton(loggerFactory)
            .AddLogging()
            .AddCoachDbInfrastructure()
            .AddIdentityModule(configuration)
            .AddTenantsModule(configuration);

        await using var provider = services.BuildServiceProvider();
        var logger = loggerFactory.CreateLogger(typeof(MigrationRunner));

        foreach (var contextType in Modules)
        {
            await using var scope = provider.CreateAsyncScope();
            var context = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(contextType);

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            logger.LogInformation("{Schema}: {Count} pending migration(s) {Migrations}", context.Schema, pending.Count, pending);

            await context.Database.MigrateAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync(GrantSql(context.Schema), cancellationToken);
        }

        logger.LogInformation("Database is up to date");
    }

    // Roles that don't exist (e.g. a bare test database) are skipped. The schema name comes from code
    // and is validated, because it cannot be a SQL parameter.
    internal static string GrantSql(string schema)
    {
        if (schema.Length == 0 || !schema.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            throw new ArgumentException($"Invalid schema name '{schema}'.", nameof(schema));

        return $$"""
            DO $grant$
            DECLARE r text;
            BEGIN
              FOREACH r IN ARRAY ARRAY['app_user', 'app_system'] LOOP
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
                  EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', '{{schema}}', r);
                  EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', '{{schema}}', r);
                  EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I', '{{schema}}', r);
                END IF;
              END LOOP;
              IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_readonly') THEN
                EXECUTE format('GRANT USAGE ON SCHEMA %I TO app_readonly', '{{schema}}');
                EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA %I TO app_readonly', '{{schema}}');
              END IF;
            END
            $grant$;
            """;
    }
}
