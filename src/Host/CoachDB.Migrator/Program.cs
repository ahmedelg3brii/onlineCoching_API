using CoachDB.Migrator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// Connection: --connection "<cs>", or env COACHDB_MIGRATOR_CONNECTION, or the local docker-compose default.
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var connectionString = configuration["connection"]
    ?? configuration["COACHDB_MIGRATOR_CONNECTION"]
    ?? "Host=localhost;Port=5433;Database=coachdb;Username=app_migrator;Password=app_migrator_dev";

using var loggerFactory = LoggerFactory.Create(logging => logging
    .AddSimpleConsole(o => o.SingleLine = true)
    .SetMinimumLevel(LogLevel.Information)
    .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning)
    // On a fresh database EF probes the missing history table and logs it as an error; real failures
    // still surface as an exception below.
    .AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None));

try
{
    await MigrationRunner.RunAsync(connectionString, loggerFactory, CancellationToken.None);
    return 0;
}
catch (Exception ex)
{
    loggerFactory.CreateLogger("Migrator").LogCritical(ex, "Migration failed");
    return 1;
}
