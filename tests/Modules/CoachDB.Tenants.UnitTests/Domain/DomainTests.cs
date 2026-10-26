using CoachDB.SharedKernel.Domain;
using CoachDB.Tenants.Contracts.Events;
using CoachDB.Tenants.Domain.Billing;
using CoachDB.Tenants.Domain.Plans;
using CoachDB.Tenants.Domain.Tenants;

namespace CoachDB.Tenants.UnitTests.Domain;

public sealed class SlugTests
{
    [Theory]
    [InlineData("fitwithahmed")]
    [InlineData("fit-with-ahmed")]
    [InlineData("abc")]
    [InlineData("gym24")]
    [InlineData("  MixedCase  ", "mixedcase")]
    public void Valid_slugs_are_normalized(string input, string? expected = null) =>
        Slug.Create(input).Value.Value.ShouldBe(expected ?? input);

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("-start")]
    [InlineData("end-")]
    [InlineData("double--hyphen")]
    [InlineData("xn--punycode")]
    [InlineData("under_score")]
    [InlineData("dot.ted")]
    [InlineData("عربي")]
    public void Malformed_slugs_are_invalid(string input) =>
        Slug.Create(input).Error.Code.ShouldBe("slug_invalid");

    [Fact]
    public void Max_length_is_a_dns_label()
    {
        Slug.Create(new string('a', Slug.MaxLength)).IsSuccess.ShouldBeTrue();
        Slug.Create(new string('a', Slug.MaxLength + 1)).Error.Code.ShouldBe("slug_invalid");
    }

    [Theory]
    [InlineData("www")]
    [InlineData("api")]
    [InlineData("ADMIN")]
    public void Platform_names_are_reserved(string input) =>
        Slug.Create(input).Error.Code.ShouldBe("slug_reserved");
}

public sealed class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Plan = Guid.CreateVersion7();

    private static Tenant NewTenant(string? currency = null, string? timezone = null, string? locale = null) =>
        Tenant.StartTrial(" Fit With Ahmed ", Slug.Create("fitwithahmed").Value, Owner, Plan, TimeSpan.FromDays(14), Now, currency, timezone, locale);

    [Fact]
    public void Starts_a_trial_with_defaults_and_raises_TenantCreated()
    {
        var tenant = NewTenant();

        tenant.Id.Version.ShouldBe(7);
        tenant.Name.ShouldBe("Fit With Ahmed");
        tenant.Status.ShouldBe(TenantStatus.Trial);
        tenant.PlanId.ShouldBe(Plan);
        tenant.TrialEndsAt.ShouldBe(Now.AddDays(14));
        tenant.DefaultCurrency.ShouldBe("EGP");
        tenant.Timezone.ShouldBe("Africa/Cairo");
        tenant.DefaultLocale.ShouldBe("ar");
        tenant.Branding.ShouldBe(TenantBranding.Empty);
        tenant.CreatedAt.ShouldBe(Now);
        tenant.IsAccessible.ShouldBeTrue();

        var created = tenant.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TenantCreated>();
        created.TenantId.ShouldBe(tenant.Id);
        created.OwnerUserId.ShouldBe(Owner);
        created.Slug.ShouldBe("fitwithahmed");
    }

    [Fact]
    public void Explicit_region_settings_are_normalized() =>
        NewTenant(currency: " sar ", timezone: "Asia/Riyadh", locale: "en").DefaultCurrency.ShouldBe("SAR");

    [Fact]
    public void Update_profile_changes_only_what_was_sent()
    {
        var tenant = NewTenant();
        var later = Now.AddHours(1);

        tenant.UpdateProfile(name: null, currency: "usd", timezone: null, locale: null, later);

        tenant.Name.ShouldBe("Fit With Ahmed");
        tenant.DefaultCurrency.ShouldBe("USD");
        tenant.UpdatedAt.ShouldBe(later);
    }

    [Fact]
    public void Owners_cannot_set_the_custom_domain()
    {
        var tenant = NewTenant();

        tenant.UpdateSettings(new TenantSettings("a@b.com", null, null, null, null, CustomDomain: "evil.example.com"), Now);

        tenant.Settings.ContactEmail.ShouldBe("a@b.com");
        tenant.Settings.CustomDomain.ShouldBeNull();
    }

    [Fact]
    public void Suspend_and_reactivate_are_idempotent_and_raise_one_event_each()
    {
        var tenant = NewTenant();
        tenant.ClearDomainEvents();

        tenant.Suspend(Now);
        tenant.Suspend(Now);
        tenant.IsAccessible.ShouldBeFalse();

        tenant.Reactivate(Now.AddDays(1));
        tenant.Reactivate(Now.AddDays(1));

        tenant.Status.ShouldBe(TenantStatus.Trial, "the trial has not ended yet");
        tenant.DomainEvents.Select(e => e.GetType()).ShouldBe([typeof(TenantSuspended), typeof(TenantReactivated)]);
    }

    [Fact]
    public void Reactivating_after_the_trial_makes_the_tenant_active()
    {
        var tenant = NewTenant();
        tenant.Suspend(Now);

        tenant.Reactivate(Now.AddDays(30));

        tenant.Status.ShouldBe(TenantStatus.Active);
    }
}

public sealed class PlatformPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static Money Usd(decimal amount) => Money.Create(amount, "USD").Value;

    [Fact]
    public void Create_normalizes_code_and_sorts_features()
    {
        var plan = PlatformPlan.Create(" PRO ", " Pro ", Usd(49), 100, 3,
            new Dictionary<string, bool> { ["training"] = true, ["chat"] = true }, Now).Value;

        plan.Code.ShouldBe("pro");
        plan.Name.ShouldBe("Pro");
        plan.IsActive.ShouldBeTrue();
        plan.Features.Keys.ShouldBe(["chat", "training"]);
        plan.Has(PlanFeatures.Training).ShouldBeTrue();
        plan.Has(PlanFeatures.CustomDomain).ShouldBeFalse("a missing flag is off");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("has space")]
    [InlineData("ümlaut")]
    public void Invalid_codes_are_rejected(string code) =>
        PlatformPlan.Create(code, "X", Usd(1), null, null, new Dictionary<string, bool>(), Now).Error.Code.ShouldBe("plan_code_invalid");

    [Theory]
    [InlineData("training", true)]
    [InlineData("custom_domain", true)]
    [InlineData("v2", true)]
    [InlineData("Training", false)]
    [InlineData("2fa", false)]
    [InlineData("with-dash", false)]
    [InlineData("", false)]
    public void Feature_keys_are_snake_case(string key, bool valid) =>
        PlatformPlan.IsValidFeatureKey(key).ShouldBe(valid);

    [Fact]
    public void SetActive_only_touches_updatedAt_on_a_change()
    {
        var plan = PlatformPlan.Create("pro", "Pro", Usd(49), null, null, new Dictionary<string, bool>(), Now).Value;

        plan.SetActive(true, Now.AddDays(1));
        plan.UpdatedAt.ShouldBe(Now);

        plan.SetActive(false, Now.AddDays(2));
        plan.IsActive.ShouldBeFalse();
        plan.UpdatedAt.ShouldBe(Now.AddDays(2));
    }
}

public sealed class BillingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Trial_subscription_is_current_until_its_end()
    {
        var s = TenantPlanSubscription.StartTrial(Guid.CreateVersion7(), Guid.CreateVersion7(), Now, Now.AddDays(14));

        s.Status.ShouldBe(PlanSubscriptionStatus.Trialing);
        s.IsCurrent.ShouldBeTrue();
        s.CurrentPeriodStart.ShouldBe(Now);
        s.CurrentPeriodEnd.ShouldBe(Now.AddDays(14));
    }

    [Fact]
    public void Payment_account_trims_its_id_and_starts_pending()
    {
        var account = TenantPaymentAccount.Connect(Guid.CreateVersion7(), PaymentProvider.Paymob, "  m-1 ", isDefault: false, Now);

        account.ProviderAccountId.ShouldBe("m-1");
        account.Status.ShouldBe(PaymentAccountStatus.Pending);
        TenantPaymentAccount.Connect(Guid.CreateVersion7(), PaymentProvider.Stripe, "  ", false, Now).ProviderAccountId.ShouldBeNull();
    }

    [Fact]
    public void Default_flag_moves_with_make_and_clear()
    {
        var account = TenantPaymentAccount.Connect(Guid.CreateVersion7(), PaymentProvider.Paymob, null, isDefault: false, Now);

        account.MakeDefault(Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        account.IsDefault.ShouldBeTrue();
        account.UpdatedAt.ShouldBe(Now.AddHours(1));

        account.ClearDefault(Now.AddHours(2));
        account.IsDefault.ShouldBeFalse();
    }
}
