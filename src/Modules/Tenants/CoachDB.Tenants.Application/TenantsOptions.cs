namespace CoachDB.Tenants.Application;

/// <summary>Configuration section <c>Tenants</c>.</summary>
public sealed class TenantsOptions
{
    public const string Section = "Tenants";

    /// <summary>Length of the free trial a new business starts with.</summary>
    public int TrialDays { get; set; } = 14;

    /// <summary>Code of the <c>platform_plans</c> row whose limits apply during the trial.</summary>
    public string TrialPlanCode { get; set; } = "starter";

    /// <summary>How many businesses one user may own; stops sign-up abuse.</summary>
    public int MaxOwnedTenantsPerUser { get; set; } = 3;
}
