using CoachDB.SharedKernel.Domain;
using CoachDB.SharedKernel.Results;

namespace CoachDB.Tenants.Domain.Plans;

/// <summary>
/// A SaaS tier the platform sells to coaches (<c>platform_plans</c>). What a tier includes is data, not code:
/// a new tier is a new row, and features are flags in <see cref="Features"/>.
/// </summary>
public sealed class PlatformPlan : AggregateRoot<Guid>, IHasTimestamps
{
    public const int MaxCodeLength = 50;
    public const int MaxNameLength = 100;
    public const int MaxFeatures = 50;

    private PlatformPlan() { } // EF Core

    private PlatformPlan(Guid id, string code) : base(id) => Code = code;

    /// <summary>Stable identifier used in config and by other modules (<c>starter</c>, <c>pro</c>). Never changes.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
    public Money MonthlyPrice { get; private set; } = null!;

    /// <summary>null = unlimited.</summary>
    public int? MaxClients { get; private set; }

    /// <summary>null = unlimited. Owners count as coaches.</summary>
    public int? MaxCoaches { get; private set; }

    public IReadOnlyDictionary<string, bool> Features { get; private set; } = new Dictionary<string, bool>();
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<PlatformPlan> Create(
        string code, string name, Money monthlyPrice, int? maxClients, int? maxCoaches,
        IReadOnlyDictionary<string, bool> features, DateTimeOffset now)
    {
        var normalized = code.Trim().ToLowerInvariant();
        if (!IsValidCode(normalized))
            return TenantsErrors.PlanCodeInvalid;

        var plan = new PlatformPlan(Guid.CreateVersion7(), normalized) { IsActive = true, CreatedAt = now };
        plan.Update(name, monthlyPrice, maxClients, maxCoaches, features, now);
        return plan;
    }

    /// <summary>Replaces everything but the code (PUT semantics). Inputs are validated by the caller.</summary>
    public void Update(
        string name, Money monthlyPrice, int? maxClients, int? maxCoaches,
        IReadOnlyDictionary<string, bool> features, DateTimeOffset now)
    {
        Name = name.Trim();
        MonthlyPrice = monthlyPrice;
        MaxClients = maxClients;
        MaxCoaches = maxCoaches;
        Features = new SortedDictionary<string, bool>(features.ToDictionary(), StringComparer.Ordinal);
        UpdatedAt = now;
    }

    /// <summary>Inactive plans are hidden from the price list and can't start new trials; existing tenants keep them.</summary>
    public void SetActive(bool active, DateTimeOffset now)
    {
        if (IsActive == active)
            return;

        IsActive = active;
        UpdatedAt = now;
    }

    public bool Has(string feature) => Features.TryGetValue(feature, out var on) && on;

    public static bool IsValidCode(string code) =>
        code.Length is >= 2 and <= MaxCodeLength
        && code.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_');

    /// <summary>Feature keys: snake_case, 1-50 chars, starting with a letter.</summary>
    public static bool IsValidFeatureKey(string key) =>
        key.Length is >= 1 and <= 50
        && char.IsAsciiLetterLower(key[0])
        && key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');
}

/// <summary>Feature flag names the code checks. Plans may hold other keys too; unknown keys are simply off.</summary>
public static class PlanFeatures
{
    public const string Nutrition = "nutrition";
    public const string Training = "training";
    public const string Chat = "chat";
    public const string CustomBranding = "custom_branding";
    public const string CustomDomain = "custom_domain";
}
