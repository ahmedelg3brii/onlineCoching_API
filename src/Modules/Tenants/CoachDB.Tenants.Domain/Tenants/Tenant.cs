using CoachDB.SharedKernel.Domain;
using CoachDB.Tenants.Contracts.Events;

namespace CoachDB.Tenants.Domain.Tenants;

public enum TenantStatus
{
    Trial,
    Active,
    PastDue,
    Suspended,
    Cancelled,
}

/// <summary>
/// A coaching business: the unit of isolation. Every tenant-owned row in every module carries its id.
/// The tenant itself is global (not tenant-owned): it is found by slug before a tenant is chosen.
/// </summary>
public sealed class Tenant : AggregateRoot<Guid>, IHasTimestamps, ISoftDeletable
{
    public static readonly string[] SupportedLocales = ["ar", "en"];
    public const string DefaultLocaleCode = "ar";
    public const string DefaultCurrencyCode = "EGP";
    public const string DefaultTimezone = "Africa/Cairo";
    public const int MaxNameLength = 200;

    private Tenant() { } // EF Core

    private Tenant(Guid id, string name, string slug, Guid planId, string currency, string timezone, string locale,
        DateTimeOffset trialEndsAt, DateTimeOffset now) : base(id)
    {
        Name = name;
        Slug = slug;
        Status = TenantStatus.Trial;
        PlanId = planId;
        DefaultCurrency = currency;
        Timezone = timezone;
        DefaultLocale = locale;
        TrialEndsAt = trialEndsAt;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Lower-case, validated by the Slug value object. Stored as text so it can be sorted and compared in SQL.</summary>
    public string Slug { get; private set; } = string.Empty;

    public TenantStatus Status { get; private set; }

    /// <summary>The SaaS plan whose limits apply now (the trial plan during the trial).</summary>
    public Guid? PlanId { get; private set; }

    public string DefaultCurrency { get; private set; } = DefaultCurrencyCode;
    public string Timezone { get; private set; } = DefaultTimezone;
    public string DefaultLocale { get; private set; } = DefaultLocaleCode;
    public TenantBranding Branding { get; private set; } = TenantBranding.Empty;
    public TenantSettings Settings { get; private set; } = TenantSettings.Empty;
    public DateTimeOffset? TrialEndsAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Members and clients can use the business (the trial or a paid plan, even if a payment is late).</summary>
    public bool IsAccessible => DeletedAt is null && Status is not (TenantStatus.Suspended or TenantStatus.Cancelled);

    /// <summary>
    /// Starts a business on a free trial of <paramref name="trialPlanId"/>. The caller has checked the slug is free
    /// and makes <paramref name="ownerUserId"/> its owner (a <c>TenantMember</c>).
    /// </summary>
    public static Tenant StartTrial(
        string name, Slug slug, Guid ownerUserId, Guid trialPlanId, TimeSpan trialLength, DateTimeOffset now,
        string? currency = null, string? timezone = null, string? locale = null)
    {
        var tenant = new Tenant(
            Guid.CreateVersion7(),
            name.Trim(),
            slug.Value,
            trialPlanId,
            NormalizeCurrency(currency) ?? DefaultCurrencyCode,
            string.IsNullOrWhiteSpace(timezone) ? DefaultTimezone : timezone.Trim(),
            NormalizeLocale(locale) ?? DefaultLocaleCode,
            now + trialLength,
            now);

        tenant.Raise(new TenantCreated(tenant.Id, tenant.Slug, tenant.Name, ownerUserId, tenant.TrialEndsAt!.Value));
        return tenant;
    }

    /// <summary>PATCH semantics: a null argument leaves that value unchanged. Inputs are validated by the caller.</summary>
    public void UpdateProfile(string? name, string? currency, string? timezone, string? locale, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(name))
            Name = name.Trim();
        if (NormalizeCurrency(currency) is { } c)
            DefaultCurrency = c;
        if (!string.IsNullOrWhiteSpace(timezone))
            Timezone = timezone.Trim();
        if (NormalizeLocale(locale) is { } l)
            DefaultLocale = l;
        UpdatedAt = now;
    }

    public void UpdateBranding(TenantBranding branding, DateTimeOffset now)
    {
        Branding = branding;
        UpdatedAt = now;
    }

    /// <summary>Replaces the contact settings. The custom domain is kept: only the platform sets it.</summary>
    public void UpdateSettings(TenantSettings settings, DateTimeOffset now)
    {
        Settings = settings with { CustomDomain = Settings.CustomDomain };
        UpdatedAt = now;
    }

    /// <summary>Platform admin action. Idempotent.</summary>
    public void Suspend(DateTimeOffset now)
    {
        if (Status == TenantStatus.Suspended)
            return;

        Status = TenantStatus.Suspended;
        UpdatedAt = now;
        Raise(new TenantSuspended(Id));
    }

    /// <summary>Back to the trial if it hasn't ended, otherwise active (billing decides later if payment is due). Idempotent.</summary>
    public void Reactivate(DateTimeOffset now)
    {
        if (Status != TenantStatus.Suspended)
            return;

        Status = TrialEndsAt > now ? TenantStatus.Trial : TenantStatus.Active;
        UpdatedAt = now;
        Raise(new TenantReactivated(Id));
    }

    private static string? NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();

    private static string? NormalizeLocale(string? locale) =>
        locale is not null && SupportedLocales.Contains(locale) ? locale : null;
}

/// <summary>How the business looks on its public pages and in the client app. Stored as <c>tenants.branding</c> (jsonb).</summary>
public sealed record TenantBranding(
    string? LogoUrl,
    string? CoverImageUrl,
    string? PrimaryColor,
    string? AccentColor,
    string? Tagline)
{
    public static readonly TenantBranding Empty = new(null, null, null, null, null);
}

/// <summary>Contact details and platform-managed settings. Stored as <c>tenants.settings</c> (jsonb).</summary>
/// <param name="CustomDomain">Set by the platform (Phase 3), e.g. <c>coach.example.com</c>; owners can't change it.</param>
public sealed record TenantSettings(
    string? ContactEmail,
    string? ContactPhone,
    string? WhatsApp,
    string? InstagramUrl,
    string? WebsiteUrl,
    string? CustomDomain)
{
    public static readonly TenantSettings Empty = new(null, null, null, null, null, null);
}
