using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace CoachDB.Api.Setup;

internal static class HealthSetup
{
    /// <summary>Checks tagged "ready" (database, Redis…) gate <c>/health/ready</c>; <c>/health/live</c> only proves the process answers.</summary>
    public const string ReadyTag = "ready";

    public static void MapCoachDbHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });
    }
}
