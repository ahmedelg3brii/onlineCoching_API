using CoachDB.Application.Abstractions.Authorization;
using CoachDB.Application.Abstractions.Dtos;
using CoachDB.Tenants.Application.Admin;
using CoachDB.Tenants.Application.Authorization;
using CoachDB.Tenants.Application.Tenants;
using CoachDB.Tenants.Application.Tenants.Create;
using CoachDB.Tenants.Application.Tenants.Update;
using CoachDB.Tenants.Domain.Members;
using FluentValidation;

namespace CoachDB.Tenants.UnitTests.Application;

internal static class ValidatorExtensions
{
    public static Dictionary<string, string[]> Errors<T>(this IValidator<T> validator, T message) =>
        validator.Validate(message).Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
}

public sealed class RolePermissionsTests
{
    [Fact]
    public void Only_the_owner_manages_the_business_and_its_billing()
    {
        RolePermissions.For(MemberRole.Owner).ShouldContain(Permissions.Tenant.Manage);
        RolePermissions.For(MemberRole.Owner).ShouldContain(Permissions.Tenant.Billing);
        RolePermissions.For(MemberRole.Coach).ShouldNotContain(Permissions.Tenant.Billing);
        RolePermissions.For(MemberRole.Assistant).ShouldNotContain(Permissions.Tenant.Manage);
    }

    [Fact]
    public void Assistants_write_drafts_only_and_coaches_see_assigned_clients_only()
    {
        RolePermissions.For(MemberRole.Assistant).ShouldContain(Permissions.Plans.WriteDraft);
        RolePermissions.For(MemberRole.Assistant).ShouldNotContain(Permissions.Plans.Write);
        RolePermissions.For(MemberRole.Coach).ShouldNotContain(Permissions.Clients.ReadAll);
        RolePermissions.For(MemberRole.Owner).ShouldContain(Permissions.Clients.ReadAll);
    }

    [Fact]
    public void Every_role_has_a_permission_set() =>
        Enum.GetValues<MemberRole>().ShouldAllBe(role => RolePermissions.For(role).Count > 0);
}

public sealed class CreateTenantValidatorTests
{
    private static readonly CreateTenantValidator Validator = new();
    private static readonly CreateTenantCommand Valid = new("Fit With Ahmed", "fitwithahmed", "EGP", "Africa/Cairo", "ar");

    [Fact]
    public void Valid_command_has_no_errors() => Validator.Errors(Valid).ShouldBeEmpty();

    [Fact]
    public void Optional_region_fields_may_be_left_out() =>
        Validator.Errors(Valid with { DefaultCurrency = null, Timezone = null, DefaultLocale = null }).ShouldBeEmpty();

    [Fact]
    public void Slug_errors_use_the_slug_codes()
    {
        Validator.Errors(Valid with { Slug = "" })["Slug"].ShouldBe(["required"]);
        Validator.Errors(Valid with { Slug = "a" })["Slug"].ShouldBe(["slug_invalid"]);
        Validator.Errors(Valid with { Slug = "www" })["Slug"].ShouldBe(["slug_reserved"]);
    }

    [Theory]
    [InlineData("Egypt Standard Time")] // Windows id, not IANA
    [InlineData("Mars/Olympus")]
    [InlineData("cairo")]
    public void Timezone_must_be_a_known_IANA_zone(string tz) =>
        Validator.Errors(Valid with { Timezone = tz })["Timezone"].ShouldBe(["invalid_timezone"]);

    [Fact]
    public void Utc_is_a_valid_timezone() => Validator.Errors(Valid with { Timezone = "UTC" }).ShouldBeEmpty();

    [Fact]
    public void Name_is_required_and_bounded()
    {
        Validator.Errors(Valid with { Name = "" })["Name"].ShouldBe(["required"]);
        Validator.Errors(Valid with { Name = new string('x', 201) })["Name"].ShouldBe(["too_long"]);
    }
}

public sealed class UpdateTenantValidatorTests
{
    private static readonly UpdateTenantValidator Validator = new();
    private static readonly UpdateTenantCommand Empty = new(null, null, null, null, null, null);

    [Fact]
    public void An_empty_patch_is_valid() => Validator.Errors(Empty).ShouldBeEmpty();

    [Fact]
    public void Sent_name_must_not_be_blank() => Validator.Errors(Empty with { Name = "  " })["Name"].ShouldBe(["required"]);

    [Fact]
    public void Branding_needs_https_urls_and_hex_colors()
    {
        var errors = Validator.Errors(Empty with
        {
            Branding = new TenantBrandingDto("http://x.example.com/logo.png", "not a url", "#12345", "#ABCDEF", null),
        });

        errors["Branding.LogoUrl"].ShouldBe(["invalid_url"]);
        errors["Branding.CoverImageUrl"].ShouldBe(["invalid_url"]);
        errors["Branding.PrimaryColor"].ShouldBe(["invalid_color"]);
        errors.ShouldNotContainKey("Branding.AccentColor");
    }

    [Fact]
    public void Contact_email_must_be_an_email() =>
        Validator.Errors(Empty with { Settings = new TenantSettingsInput("nope", null, null, null, null) })["Settings.ContactEmail"]
            .ShouldBe(["invalid_email"]);
}

public sealed class DtoMappingTests
{
    [Fact]
    public void Branding_blanks_become_null_and_colors_lower_case()
    {
        var branding = new TenantBrandingDto("  ", null, "#AABBCC", "", " Strong ").ToDomain();

        branding.LogoUrl.ShouldBeNull();
        branding.PrimaryColor.ShouldBe("#aabbcc");
        branding.AccentColor.ShouldBeNull();
        branding.Tagline.ShouldBe("Strong");
    }

    [Fact]
    public void Settings_input_never_carries_a_custom_domain() =>
        new TenantSettingsInput("A@B.COM", null, null, null, null).ToDomain().ShouldSatisfyAllConditions(
            s => s.ContactEmail.ShouldBe("a@b.com"),
            s => s.CustomDomain.ShouldBeNull());
}

public sealed class PlanValidatorTests
{
    private static readonly CreatePlanValidator Validator = new();
    private static readonly CreatePlanCommand Valid = new("pro", "Pro", new MoneyDto(49, "USD"), 100, 3,
        new Dictionary<string, bool> { ["training"] = true });

    [Fact]
    public void Valid_command_has_no_errors() => Validator.Errors(Valid).ShouldBeEmpty();

    [Fact]
    public void Unlimited_limits_and_no_features_are_valid() =>
        Validator.Errors(Valid with { MaxClients = null, MaxCoaches = null, Features = null }).ShouldBeEmpty();

    [Fact]
    public void Price_limits_and_feature_keys_are_checked()
    {
        var errors = Validator.Errors(Valid with
        {
            MonthlyPrice = new MoneyDto(-1, "US"),
            MaxClients = 0,
            Features = new Dictionary<string, bool> { ["Not-Snake"] = true },
        });

        errors["MonthlyPrice.Amount"].ShouldBe(["must_not_be_negative"]);
        errors["MonthlyPrice.Currency"].ShouldBe(["invalid_currency"]);
        errors["MaxClients"].ShouldBe(["must_be_positive"]);
        errors["Features"].ShouldBe(["invalid_feature_key"]);
    }

    [Fact]
    public void Code_must_be_a_valid_plan_code() =>
        Validator.Errors(Valid with { Code = "has space" })["Code"].ShouldBe(["plan_code_invalid"]);
}

public sealed class TenantPageCursorTests
{
    [Fact]
    public void Cursor_round_trips_created_at_and_slug()
    {
        var tenant = CoachDB.Tenants.Domain.Tenants.Tenant.StartTrial("X", CoachDB.Tenants.Domain.Tenants.Slug.Create("my-gym").Value,
            Guid.CreateVersion7(), Guid.CreateVersion7(), TimeSpan.FromDays(1), new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero));

        var cursor = TenantPageCursor.For(tenant);

        TenantPageCursor.TryRead(cursor, out var createdAt, out var slug).ShouldBeTrue();
        createdAt.ShouldBe(tenant.CreatedAt);
        slug.ShouldBe("my-gym");
    }

    [Theory]
    [InlineData("no-separator")]
    [InlineData("abc|slug")]
    [InlineData("-5|slug")]
    public void Malformed_sort_keys_are_rejected(string sortKey) =>
        TenantPageCursor.TryRead(new CoachDB.Application.Abstractions.Paging.Cursor(sortKey, Guid.Empty), out _, out _).ShouldBeFalse();
}
