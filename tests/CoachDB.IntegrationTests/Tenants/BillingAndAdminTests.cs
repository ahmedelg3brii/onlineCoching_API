using System.Net;

namespace CoachDB.IntegrationTests.Tenants;

public sealed class PaymentAccountTests(IntegrationFixture fixture)
{
    private readonly ApiFactory _api = fixture.Factory;
    private const string Accounts = "/api/v1/tenant/payment-accounts";

    [Fact]
    public async Task Owner_connects_accounts_and_moves_the_default()
    {
        var (owner, _) = await Session.OwnerOfNewTenant(_api);

        var paymob = await owner.Post(Accounts, new { provider = "paymob", providerAccountId = "merchant-123" });
        paymob.StatusCode.ShouldBe(HttpStatusCode.Created);
        var first = await paymob.Json();
        paymob.Headers.Location?.OriginalString.ShouldBe($"{Accounts}/{first.Str("id")}");
        first.Str("status").ShouldBe("pending");
        first.GetProperty("isDefault").GetBoolean().ShouldBeTrue("the first account becomes the default");

        var stripe = await (await owner.Post(Accounts, new { provider = "stripe", makeDefault = true })).Json();
        stripe.GetProperty("isDefault").GetBoolean().ShouldBeTrue();

        var list = (await (await owner.Get(Accounts)).Json()).Items();
        list.Select(a => (a.Str("provider"), a.GetProperty("isDefault").GetBoolean()))
            .ShouldBe([("stripe", true), ("paymob", false)]);

        var back = await owner.Post($"{Accounts}/{first.Str("id")}/default");
        back.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await owner.Get($"{Accounts}/{stripe.Str("id")}")).Json()).GetProperty("isDefault").GetBoolean().ShouldBeFalse();

        await (await owner.Post(Accounts, new { provider = "paymob" })).ShouldBeProblem(HttpStatusCode.Conflict, "payment_account_exists");
        await (await owner.Post(Accounts, new { provider = "paypal" })).ShouldBeProblem(HttpStatusCode.BadRequest, "invalid_request");

        (await owner.Delete($"{Accounts}/{stripe.Str("id")}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await owner.Get($"{Accounts}/{stripe.Str("id")}")).ShouldBeProblem(HttpStatusCode.NotFound, "payment_account_not_found");
    }

    [Fact]
    public async Task Another_business_cannot_see_or_touch_an_account()
    {
        var (ownerA, _) = await Session.OwnerOfNewTenant(_api);
        var (ownerB, _) = await Session.OwnerOfNewTenant(_api);
        var account = await (await ownerA.Post(Accounts, new { provider = "paymob" })).Json();
        var id = account.Str("id");

        await (await ownerB.Get($"{Accounts}/{id}")).ShouldBeProblem(HttpStatusCode.NotFound, "payment_account_not_found");
        await (await ownerB.Post($"{Accounts}/{id}/default")).ShouldBeProblem(HttpStatusCode.NotFound, "payment_account_not_found");
        await (await ownerB.Delete($"{Accounts}/{id}")).ShouldBeProblem(HttpStatusCode.NotFound, "payment_account_not_found");
        (await (await ownerB.Get(Accounts)).Json()).Items().ShouldBeEmpty();

        (await ownerA.Get($"{Accounts}/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Payment_accounts_need_a_tenant_and_the_billing_permission()
    {
        var (owner, _) = await Session.OwnerOfNewTenant(_api);
        var tenant = owner.Tenant;
        owner.Tenant = null;
        await (await owner.Get(Accounts)).ShouldBeProblem(HttpStatusCode.BadRequest, "tenant_required");

        // Make a coach member directly (invites come with the Team module): coaches can't manage billing.
        var coach = await Session.SignUp(_api);
        await fixture.Sql(
            """
            INSERT INTO tenants.tenant_members (id, tenant_id, user_id, role)
            SELECT gen_random_uuid(), t.id, u.id, 'coach' FROM tenants.tenants t, identity.users u
            WHERE t.slug = @p0 AND u.email = @p1
            """, tenant!, coach.Email);
        coach.Tenant = tenant;

        await (await coach.Get(Accounts)).ShouldBeProblem(HttpStatusCode.Forbidden, "permission_denied");
        await (await coach.Patch("/api/v1/tenant", new { name = "Taken over" })).ShouldBeProblem(HttpStatusCode.Forbidden, "permission_denied");
        (await coach.Get("/api/v1/tenant")).StatusCode.ShouldBe(HttpStatusCode.OK); // any member may read

        var coachPlan = await (await coach.Get("/api/v1/tenant/plan")).Json();
        coachPlan.GetProperty("usage").GetProperty("coaches").GetInt32().ShouldBe(2);
    }
}

public sealed class AdminTests(IntegrationFixture fixture)
{
    private readonly ApiFactory _api = fixture.Factory;

    [Fact]
    public async Task Only_platform_admins_reach_the_admin_api()
    {
        var (owner, _) = await Session.OwnerOfNewTenant(_api);

        await (await owner.Get("/api/v1/admin/plans")).ShouldBeProblem(HttpStatusCode.Forbidden, "permission_denied");
        await (await owner.Get("/api/v1/admin/tenants")).ShouldBeProblem(HttpStatusCode.Forbidden, "permission_denied");
        var anonymous = await _api.CreateBareClient().GetAsync("/api/v1/admin/plans", TestContext.Current.CancellationToken);
        await anonymous.ShouldBeProblem(HttpStatusCode.Unauthorized, "auth_required");
    }

    [Fact]
    public async Task Admin_manages_plans()
    {
        var admin = await Session.PlatformAdmin(fixture);
        var code = Session.NewSlug("plan")[..20];

        var created = await admin.Post("/api/v1/admin/plans", new
        {
            code = code.ToUpperInvariant(),
            name = "Gym",
            monthlyPrice = new { amount = 99.999m, currency = "usd" },
            maxClients = (int?)null,
            maxCoaches = 25,
            features = new Dictionary<string, bool> { ["training"] = true, ["custom_domain"] = true },
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var plan = await created.Json();
        plan.Str("code").ShouldBe(code);
        plan.GetProperty("monthlyPrice").GetProperty("amount").GetDecimal().ShouldBe(100.00m);
        plan.GetProperty("monthlyPrice").Str("currency").ShouldBe("USD");
        plan.GetProperty("maxClients").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        plan.GetProperty("features").GetProperty("custom_domain").GetBoolean().ShouldBeTrue();
        var url = created.Headers.Location!.OriginalString;

        await (await admin.Post("/api/v1/admin/plans", new { code, name = "Dup", monthlyPrice = new { amount = 1, currency = "USD" } }))
            .ShouldBeProblem(HttpStatusCode.Conflict, "plan_code_taken");

        var invalid = await admin.Post("/api/v1/admin/plans", new
        {
            code = "no spaces",
            name = "",
            monthlyPrice = new { amount = -1, currency = "US" },
            maxCoaches = 0,
            features = new Dictionary<string, bool> { ["Bad-Key"] = true },
        });
        var problem = await invalid.ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        problem.Errors("code").ShouldContain("plan_code_invalid");
        problem.Errors("name").ShouldContain("required");
        problem.Errors("monthlyPrice.amount").ShouldContain("must_not_be_negative");
        problem.Errors("monthlyPrice.currency").ShouldContain("invalid_currency");
        problem.Errors("maxCoaches").ShouldContain("must_be_positive");
        problem.Errors("features").ShouldContain("invalid_feature_key");

        var updated = await admin.Put(url, new { name = "Gym XL", monthlyPrice = new { amount = 150, currency = "USD" }, maxClients = 500, maxCoaches = 25 });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await updated.Json();
        after.Str("name").ShouldBe("Gym XL");
        after.GetProperty("maxClients").GetInt32().ShouldBe(500);
        after.GetProperty("features").EnumerateObject().ShouldBeEmpty();

        (await (await admin.Post($"{url}/deactivate")).Json()).GetProperty("isActive").GetBoolean().ShouldBeFalse();
        var publicList = await (await _api.CreateBareClient().GetAsync("/api/v1/plans", TestContext.Current.CancellationToken)).Json();
        publicList.Items().Select(p => p.Str("code")).ShouldNotContain(code);
        (await (await admin.Get("/api/v1/admin/plans")).Json()).Items().Select(p => p.Str("code")).ShouldContain(code);

        (await (await admin.Post($"{url}/activate")).Json()).GetProperty("isActive").GetBoolean().ShouldBeTrue();
        await (await admin.Get($"/api/v1/admin/plans/{Guid.CreateVersion7()}")).ShouldBeProblem(HttpStatusCode.NotFound, "plan_not_found");
    }

    [Fact]
    public async Task Admin_lists_tenants_with_cursor_paging_and_filters()
    {
        var admin = await Session.PlatformAdmin(fixture);
        var marker = $"Paging {Guid.CreateVersion7():N}";
        var owner = await Session.SignUp(_api);
        var slugs = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var slug = Session.NewSlug();
            slugs.Add(slug);
            (await owner.Post("/api/v1/tenants", new { name = $"{marker} {i}", slug })).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = $"/api/v1/admin/tenants?limit=2&search={Uri.EscapeDataString(marker.ToLowerInvariant())}" + (cursor is null ? "" : $"&cursor={cursor}");
            var page = await (await admin.Get(url)).Json();
            seen.AddRange(page.Items().Select(t => t.Str("slug")));
            cursor = page.GetProperty("nextCursor").GetString();
            page.GetProperty("hasMore").GetBoolean().ShouldBe(cursor is not null);
            pages++;
        }
        while (cursor is not null);

        pages.ShouldBe(2);
        seen.ShouldBe(Enumerable.Reverse(slugs).ToList(), "newest first, every tenant exactly once");

        var trial = await (await admin.Get($"/api/v1/admin/tenants?status=trial&search={Uri.EscapeDataString(marker)}")).Json();
        trial.Items().Length.ShouldBe(3);
        var suspended = await (await admin.Get($"/api/v1/admin/tenants?status=suspended&search={Uri.EscapeDataString(marker)}")).Json();
        suspended.Items().ShouldBeEmpty();

        await (await admin.Get("/api/v1/admin/tenants?status=frozen")).ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        await (await admin.Get("/api/v1/admin/tenants?cursor=garbage")).ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Suspending_a_business_locks_out_its_members_until_reactivated()
    {
        var admin = await Session.PlatformAdmin(fixture);
        var (owner, tenant) = await Session.OwnerOfNewTenant(_api);
        var id = tenant.Str("id");

        var suspended = await admin.Post($"/api/v1/admin/tenants/{id}/suspend");
        (await suspended.Json()).Str("status").ShouldBe("suspended");

        await (await owner.Get("/api/v1/tenant")).ShouldBeProblem(HttpStatusCode.Forbidden, "tenant_suspended");
        (await (await owner.Get("/api/v1/me/tenants")).Json()).Items().ShouldBeEmpty();
        var publicPage = await _api.CreateBareClient().GetAsync($"/api/v1/public/{tenant.Str("slug")}", TestContext.Current.CancellationToken);
        await publicPage.ShouldBeProblem(HttpStatusCode.NotFound, "tenant_not_found");

        var reactivated = await admin.Post($"/api/v1/admin/tenants/{id}/reactivate");
        (await reactivated.Json()).Str("status").ShouldBe("trial");
        (await owner.Get("/api/v1/tenant")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await (await admin.Post($"/api/v1/admin/tenants/{Guid.CreateVersion7()}/suspend")).ShouldBeProblem(HttpStatusCode.NotFound, "tenant_not_found");
    }
}
