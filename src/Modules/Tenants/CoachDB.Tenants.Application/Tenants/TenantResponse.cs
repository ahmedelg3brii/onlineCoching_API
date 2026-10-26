using CoachDB.Tenants.Domain.Members;
using CoachDB.Tenants.Domain.Tenants;

namespace CoachDB.Tenants.Application.Tenants;

/// <summary>A business as its members (and platform admins) see it. Shared by create, get, update and the admin API.</summary>
public sealed record TenantResponse(
    Guid Id,
    string Name,
    string Slug,
    TenantStatus Status,
    Guid? PlanId,
    string DefaultCurrency,
    string Timezone,
    string DefaultLocale,
    TenantBrandingDto Branding,
    TenantSettingsDto Settings,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static TenantResponse From(Tenant tenant) => new(
        tenant.Id,
        tenant.Name,
        tenant.Slug,
        tenant.Status,
        tenant.PlanId,
        tenant.DefaultCurrency,
        tenant.Timezone,
        tenant.DefaultLocale,
        TenantBrandingDto.From(tenant.Branding),
        TenantSettingsDto.From(tenant.Settings),
        tenant.TrialEndsAt,
        tenant.CreatedAt,
        tenant.UpdatedAt);
}

public sealed record TenantBrandingDto(
    string? LogoUrl,
    string? CoverImageUrl,
    string? PrimaryColor,
    string? AccentColor,
    string? Tagline)
{
    public static TenantBrandingDto From(TenantBranding b) => new(b.LogoUrl, b.CoverImageUrl, b.PrimaryColor, b.AccentColor, b.Tagline);

    public TenantBranding ToDomain() => new(Blank(LogoUrl), Blank(CoverImageUrl), Blank(PrimaryColor)?.ToLowerInvariant(),
        Blank(AccentColor)?.ToLowerInvariant(), Blank(Tagline));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <param name="CustomDomain">Read-only: set by the platform.</param>
public sealed record TenantSettingsDto(
    string? ContactEmail,
    string? ContactPhone,
    string? WhatsApp,
    string? InstagramUrl,
    string? WebsiteUrl,
    string? CustomDomain)
{
    public static TenantSettingsDto From(TenantSettings s) =>
        new(s.ContactEmail, s.ContactPhone, s.WhatsApp, s.InstagramUrl, s.WebsiteUrl, s.CustomDomain);
}

/// <summary>Contact settings an owner can change (everything but the custom domain).</summary>
public sealed record TenantSettingsInput(
    string? ContactEmail,
    string? ContactPhone,
    string? WhatsApp,
    string? InstagramUrl,
    string? WebsiteUrl)
{
    public TenantSettings ToDomain() => new(Blank(ContactEmail)?.ToLowerInvariant(), Blank(ContactPhone), Blank(WhatsApp),
        Blank(InstagramUrl), Blank(WebsiteUrl), CustomDomain: null);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>What anyone may see about a business: its public page and price list header (<c>GET /public/{slug}</c>).</summary>
public sealed record PublicTenantResponse(
    string Slug,
    string Name,
    string DefaultLocale,
    string DefaultCurrency,
    string Timezone,
    TenantBrandingDto Branding,
    PublicContactDto Contact)
{
    public static PublicTenantResponse From(Tenant t) => new(
        t.Slug, t.Name, t.DefaultLocale, t.DefaultCurrency, t.Timezone,
        TenantBrandingDto.From(t.Branding),
        new PublicContactDto(t.Settings.ContactEmail, t.Settings.ContactPhone, t.Settings.WhatsApp, t.Settings.InstagramUrl, t.Settings.WebsiteUrl));
}

public sealed record PublicContactDto(string? Email, string? Phone, string? WhatsApp, string? InstagramUrl, string? WebsiteUrl);

/// <summary>
/// One business the signed-in user belongs to (<c>GET /me/tenants</c>), with what they may do there, so the app can
/// show a business switcher and hide actions up front. Send <see cref="Slug"/> as <c>X-Tenant</c> to work in it.
/// </summary>
public sealed record MyTenantResponse(
    Guid Id,
    string Slug,
    string Name,
    TenantStatus Status,
    MemberRole Role,
    IReadOnlyCollection<string> Permissions,
    string? LogoUrl);
