using System.Net;

namespace CoachDB.IntegrationTests.Tenants;

public sealed class TenantFlowTests(IntegrationFixture fixture)
{
    private readonly ApiFactory _api = fixture.Factory;

    [Fact]
    public async Task Creating_a_business_starts_a_trial_and_makes_the_caller_its_owner()
    {
        var owner = await Session.SignUp(_api);
        var slug = Session.NewSlug();

        var created = await owner.Post("/api/v1/tenants", new { name = "  Fit With Ahmed ", slug = slug.ToUpperInvariant(), timezone = "Africa/Cairo" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.Location?.OriginalString.ShouldBe("/api/v1/tenant");
        var tenant = await created.Json();
        tenant.Str("name").ShouldBe("Fit With Ahmed");
        tenant.Str("slug").ShouldBe(slug);
        tenant.Str("status").ShouldBe("trial");
        tenant.Str("defaultCurrency").ShouldBe("EGP");
        tenant.Str("defaultLocale").ShouldBe("ar");
        tenant.GetProperty("planId").GetGuid().ShouldNotBe(Guid.Empty);
        tenant.GetProperty("trialEndsAt").GetDateTimeOffset().ShouldBe(DateTimeOffset.UtcNow.AddDays(14), TimeSpan.FromMinutes(5));
        tenant.GetProperty("branding").GetProperty("logoUrl").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        var mine = (await (await owner.Get("/api/v1/me/tenants")).Json()).Items();
        var membership = mine.ShouldHaveSingleItem();
        membership.Str("slug").ShouldBe(slug);
        membership.Str("role").ShouldBe("owner");
        membership.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ShouldContain("tenant.manage");

        owner.Tenant = slug;
        var current = await owner.Get("/api/v1/tenant");
        current.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await current.Json()).Str("id").ShouldBe(tenant.Str("id"));

        var plan = await (await owner.Get("/api/v1/tenant/plan")).Json();
        plan.GetProperty("plan").Str("code").ShouldBe("starter");
        plan.GetProperty("plan").GetProperty("features").GetProperty("nutrition").GetBoolean().ShouldBeTrue();
        plan.GetProperty("subscription").Str("status").ShouldBe("trialing");
        plan.GetProperty("usage").GetProperty("coaches").GetInt32().ShouldBe(1);
        plan.GetProperty("usage").GetProperty("maxCoaches").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_business_gets_field_error_codes()
    {
        var owner = await Session.SignUp(_api);

        var reserved = await owner.Post("/api/v1/tenants", new { name = "", slug = "admin", timezone = "Mars/Olympus", defaultCurrency = "EGPX", defaultLocale = "fr" });

        var problem = await reserved.ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        problem.Errors("name").ShouldContain("required");
        problem.Errors("slug").ShouldContain("slug_reserved");
        problem.Errors("timezone").ShouldContain("invalid_timezone");
        problem.Errors("defaultCurrency").ShouldContain("invalid_currency");
        problem.Errors("defaultLocale").ShouldContain("unsupported_locale");

        var malformed = await owner.Post("/api/v1/tenants", new { name = "X", slug = "-bad--slug" });
        (await malformed.ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed")).Errors("slug").ShouldContain("slug_invalid");
    }

    [Fact]
    public async Task A_slug_can_be_used_once_and_the_availability_check_says_why()
    {
        var (_, tenant) = await Session.OwnerOfNewTenant(_api);
        var slug = tenant.Str("slug");
        var other = await Session.SignUp(_api);

        await (await other.Post("/api/v1/tenants", new { name = "Copycat", slug })).ShouldBeProblem(HttpStatusCode.Conflict, "slug_taken");

        var client = _api.CreateBareClient();
        async Task<(bool Available, string? Reason)> Check(string s)
        {
            var body = await (await client.GetAsync($"/api/v1/tenants/slug-availability?slug={s}", TestContext.Current.CancellationToken)).Json();
            return (body.GetProperty("available").GetBoolean(), body.GetProperty("reason").GetString());
        }

        (await Check(slug)).ShouldBe((false, "slug_taken"));
        (await Check("api")).ShouldBe((false, "slug_reserved"));
        (await Check("a")).ShouldBe((false, "slug_invalid"));
        (await Check(Session.NewSlug("free"))).ShouldBe((true, null));
    }

    [Fact]
    public async Task Tenant_resolution_rejects_unknown_businesses_and_non_members()
    {
        var (owner, _) = await Session.OwnerOfNewTenant(_api);
        var stranger = await Session.SignUp(_api);

        stranger.Tenant = owner.Tenant;
        await (await stranger.Get("/api/v1/tenant")).ShouldBeProblem(HttpStatusCode.Forbidden, "tenant_access_denied");

        stranger.Tenant = Session.NewSlug("nobody");
        await (await stranger.Get("/api/v1/tenant")).ShouldBeProblem(HttpStatusCode.NotFound, "tenant_not_found");

        stranger.Tenant = "not a slug!";
        await (await stranger.Get("/api/v1/tenant")).ShouldBeProblem(HttpStatusCode.NotFound, "tenant_not_found");

        stranger.Tenant = null;
        await (await stranger.Get("/api/v1/tenant")).ShouldBeProblem(HttpStatusCode.BadRequest, "tenant_required");
    }

    [Fact]
    public async Task Tenant_agnostic_endpoints_ignore_the_tenant_header()
    {
        var (owner, _) = await Session.OwnerOfNewTenant(_api);

        // The app keeps sending X-Tenant; creating a second business and listing businesses must still work.
        var second = await owner.Post("/api/v1/tenants", new { name = "Second gym", slug = Session.NewSlug() });
        second.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await (await owner.Get("/api/v1/me/tenants")).Json()).Items().Length.ShouldBe(2);
    }

    [Fact]
    public async Task Owner_updates_profile_branding_and_settings_with_patch()
    {
        var (owner, before) = await Session.OwnerOfNewTenant(_api);

        var patched = await owner.Patch("/api/v1/tenant", new
        {
            name = "Ahmed Coaching",
            defaultLocale = "en",
            branding = new { logoUrl = "https://cdn.example.com/logo.png", primaryColor = "#1A2B3C", tagline = "Get strong" },
            settings = new { contactEmail = "Hello@Example.com", whatsApp = "+201000000000", customDomain = "hijack.example.com" },
        });

        patched.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await patched.Json();
        after.Str("name").ShouldBe("Ahmed Coaching");
        after.Str("defaultLocale").ShouldBe("en");
        after.Str("defaultCurrency").ShouldBe(before.Str("defaultCurrency")); // not sent → unchanged
        after.GetProperty("branding").Str("primaryColor").ShouldBe("#1a2b3c");
        after.GetProperty("settings").Str("contactEmail").ShouldBe("hello@example.com");
        after.GetProperty("settings").GetProperty("customDomain").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        after.GetProperty("updatedAt").GetDateTimeOffset().ShouldBeGreaterThan(before.GetProperty("updatedAt").GetDateTimeOffset());

        var reread = await (await owner.Get("/api/v1/tenant")).Json();
        reread.GetProperty("branding").Str("tagline").ShouldBe("Get strong");
        // Postgres keeps microseconds, .NET 100 ns: the stored value is the returned one, truncated.
        reread.GetProperty("updatedAt").GetDateTimeOffset().ShouldBe(after.GetProperty("updatedAt").GetDateTimeOffset(), TimeSpan.FromMilliseconds(1));

        var invalid = await owner.Patch("/api/v1/tenant", new { name = "", branding = new { primaryColor = "red", logoUrl = "http://insecure.example.com/x.png" } });
        var problem = await invalid.ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        problem.Errors("name").ShouldContain("required");
        problem.Errors("branding.primaryColor").ShouldContain("invalid_color");
        problem.Errors("branding.logoUrl").ShouldContain("invalid_url");
    }

    [Fact]
    public async Task Public_profile_is_anonymous_and_hides_internal_fields()
    {
        var (owner, tenant) = await Session.OwnerOfNewTenant(_api, "Public Gym");
        await owner.Patch("/api/v1/tenant", new { settings = new { contactPhone = "+20123" } });
        var anonymous = _api.CreateBareClient();

        var response = await anonymous.GetAsync($"/api/v1/public/{tenant.Str("slug")}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Json();
        body.Str("name").ShouldBe("Public Gym");
        body.GetProperty("contact").Str("phone").ShouldBe("+20123");
        body.TryGetProperty("id", out _).ShouldBeFalse();
        body.TryGetProperty("planId", out _).ShouldBeFalse();

        var missing = await anonymous.GetAsync($"/api/v1/public/{Session.NewSlug("ghost")}", TestContext.Current.CancellationToken);
        await missing.ShouldBeProblem(HttpStatusCode.NotFound, "tenant_not_found");
    }

    [Fact]
    public async Task A_user_can_own_at_most_three_businesses()
    {
        var owner = await Session.SignUp(_api);
        for (var i = 0; i < 3; i++)
            (await owner.Post("/api/v1/tenants", new { name = $"Gym {i}", slug = Session.NewSlug() })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var fourth = await owner.Post("/api/v1/tenants", new { name = "Gym 4", slug = Session.NewSlug() });

        await fourth.ShouldBeProblem(HttpStatusCode.Forbidden, "tenant_limit_reached");
    }

    [Fact]
    public async Task Creating_a_business_requires_sign_in()
    {
        var response = await _api.CreateBareClient().PostAsync("/api/v1/tenants",
            System.Net.Http.Json.JsonContent.Create(new { name = "x", slug = Session.NewSlug() }), TestContext.Current.CancellationToken);

        await response.ShouldBeProblem(HttpStatusCode.Unauthorized, "auth_required");
    }

    [Fact]
    public async Task Public_plan_list_shows_active_plans_cheapest_first()
    {
        var response = await _api.CreateBareClient().GetAsync("/api/v1/plans", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Json();
        page.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        var plans = page.Items();
        plans.Select(p => p.Str("code")).ShouldContain("starter");
        plans.ShouldAllBe(p => p.GetProperty("isActive").GetBoolean());
        var prices = plans.Select(p => p.GetProperty("monthlyPrice").GetProperty("amount").GetDecimal()).ToList();
        prices.ShouldBe(prices.Order().ToList());
    }
}
