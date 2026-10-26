using System.Net;
using System.Net.Http.Json;

namespace CoachDB.IntegrationTests.Identity;

public sealed class AuthFlowTests(IntegrationFixture fixture)
{
    private readonly ApiFactory _api = fixture.Factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_returns_201_with_session_cookie_and_auth_body()
    {
        var client = _api.CreateBrowserClient();
        var email = Http.NewEmail();

        var response = await client.Register(email.ToUpperInvariant(), fullName: "  Ahmed Coach ");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location?.OriginalString.ShouldBe("/api/v1/me");

        var cookie = response.RefreshSetCookie().ShouldNotBeNull().ToLowerInvariant();
        cookie.ShouldContain("httponly");
        cookie.ShouldContain("secure");
        cookie.ShouldContain("samesite=strict");
        cookie.ShouldContain("path=/api/v1/auth");
        cookie.ShouldContain("expires=");

        var body = await response.Json();
        body.GetProperty("accessToken").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("tokenType").GetString().ShouldBe("Bearer");
        body.GetProperty("expiresIn").GetInt32().ShouldBeInRange(890, 900);
        body.TryGetProperty("refreshToken", out _).ShouldBeFalse("the refresh token must only travel in the HttpOnly cookie");

        var user = body.GetProperty("user");
        user.GetProperty("email").GetString().ShouldBe(email);
        user.GetProperty("fullName").GetString().ShouldBe("Ahmed Coach");
        user.GetProperty("locale").GetString().ShouldBe("ar");
        user.GetProperty("emailVerified").GetBoolean().ShouldBeFalse();
        user.GetProperty("createdAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task Register_with_existing_email_in_other_case_returns_409_email_taken()
    {
        var client = _api.CreateBareClient();
        var email = Http.NewEmail();
        (await client.Register(email)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await client.Register(email.ToUpperInvariant());

        await response.ShouldBeProblem(HttpStatusCode.Conflict, "email_taken");
    }

    [Fact]
    public async Task Register_with_invalid_body_returns_400_with_field_error_codes()
    {
        var response = await _api.CreateBareClient().Register("not-an-email", password: "short", fullName: "");

        var body = await response.ShouldBeProblem(HttpStatusCode.BadRequest, "validation_failed");
        var errors = body.GetProperty("errors");
        errors.GetProperty("email").EnumerateArray().Select(e => e.GetString()).ShouldContain("invalid_email");
        errors.GetProperty("password").EnumerateArray().Select(e => e.GetString()).ShouldContain("too_short");
        errors.GetProperty("fullName").EnumerateArray().Select(e => e.GetString()).ShouldContain("required");
    }

    [Fact]
    public async Task Login_with_wrong_password_or_unknown_email_gives_the_same_401()
    {
        var client = _api.CreateBareClient();
        var email = Http.NewEmail();
        await client.Register(email);

        var wrongPassword = await (await client.Login(email, "Wrong-password-1")).ShouldBeProblem(HttpStatusCode.Unauthorized, "invalid_credentials");
        var unknownEmail = await (await client.Login(Http.NewEmail())).ShouldBeProblem(HttpStatusCode.Unauthorized, "invalid_credentials");

        // Nothing but the trace id may differ, or the response would reveal which emails have accounts.
        string Shape(System.Text.Json.JsonElement e) => string.Join('|', e.EnumerateObject()
            .Where(p => p.Name != "traceId").Select(p => $"{p.Name}={p.Value}"));
        Shape(wrongPassword).ShouldBe(Shape(unknownEmail));
    }

    [Fact]
    public async Task Me_requires_a_valid_access_token()
    {
        var client = _api.CreateBareClient();
        var email = Http.NewEmail();
        await client.Register(email);
        var login = await client.Login(email);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var accessToken = (await login.Json()).GetProperty("accessToken").GetString();

        var me = await client.GetMe(accessToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await me.Json();
        body.GetProperty("email").GetString().ShouldBe(email);
        body.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);

        await (await client.GetMe(null)).ShouldBeProblem(HttpStatusCode.Unauthorized, "auth_required");
        await (await client.GetMe("not-a-jwt")).ShouldBeProblem(HttpStatusCode.Unauthorized, "auth_required");
    }

    [Fact]
    public async Task Refresh_with_cookie_rotates_the_token_and_issues_a_working_access_token()
    {
        var browser = _api.CreateBrowserClient();
        var register = await browser.Register(Http.NewEmail());
        var first = register.RefreshToken();

        var refresh = await browser.PostAsync("/api/v1/auth/refresh", content: null, Ct);

        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = refresh.RefreshToken();
        second.ShouldNotBe(first);
        var accessToken = (await refresh.Json()).GetProperty("accessToken").GetString();
        (await browser.GetMe(accessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Secure_refresh_cookie_is_not_sent_over_plain_http()
    {
        // Documents why the browser client uses https://localhost: like a browser, the cookie container
        // withholds Secure cookies from http:// URIs.
        var http = _api.CreateBrowserClient(https: false);
        await http.Register(Http.NewEmail());

        var refresh = await http.PostAsync("/api/v1/auth/refresh", content: null, Ct);

        await refresh.ShouldBeProblem(HttpStatusCode.Unauthorized, "refresh_token_invalid");
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_whole_session()
    {
        var app = _api.CreateBareClient();
        var original = (await app.Register(Http.NewEmail())).RefreshToken();

        var rotated = await app.RefreshWithBody(original);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var newest = rotated.RefreshToken();

        // The old token comes back (e.g. stolen): theft is reported and the session ends.
        await (await app.RefreshWithBody(original)).ShouldBeProblem(HttpStatusCode.Unauthorized, "refresh_token_reused");

        // Even the legitimate newest token no longer works.
        await (await app.RefreshWithBody(newest)).ShouldBeProblem(HttpStatusCode.Unauthorized, "refresh_token_invalid");
    }

    [Fact]
    public async Task Logout_ends_the_session_clears_the_cookie_and_is_idempotent()
    {
        var browser = _api.CreateBrowserClient();
        var token = (await browser.Register(Http.NewEmail())).RefreshToken();

        var logout = await browser.PostAsync("/api/v1/auth/logout", content: null, Ct);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cleared = logout.RefreshSetCookie().ShouldNotBeNull();
        logout.RefreshToken().ShouldBeEmpty();
        cleared.ToLowerInvariant().ShouldContain("expires=thu, 01 jan 1970");

        // A logged-out token is simply invalid; it is not reported as theft.
        await (await _api.CreateBareClient().RefreshWithBody(token)).ShouldBeProblem(HttpStatusCode.Unauthorized, "refresh_token_invalid");

        (await browser.PostAsync("/api/v1/auth/logout", content: null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Refresh_without_any_token_returns_401_refresh_token_invalid()
    {
        var response = await _api.CreateBareClient().PostAsync("/api/v1/auth/refresh", content: null, Ct);

        await response.ShouldBeProblem(HttpStatusCode.Unauthorized, "refresh_token_invalid");
    }

    [Fact]
    public async Task Correlation_id_is_echoed_and_matches_the_problem_trace_id()
    {
        var client = _api.CreateBareClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = Http.NewEmail(), password = "Secret123!" }),
        };
        request.Headers.Add("X-Correlation-Id", "it-correlation-42");

        var response = await client.SendAsync(request, Ct);

        response.Headers.GetValues("X-Correlation-Id").Single().ShouldBe("it-correlation-42");
        var body = await response.ShouldBeProblem(HttpStatusCode.Unauthorized, "invalid_credentials");
        body.GetProperty("traceId").GetString().ShouldBe("it-correlation-42");
    }
}
