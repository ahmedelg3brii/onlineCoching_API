using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace CoachDB.IntegrationTests;

/// <summary>
/// A signed-in user talking to the API: every request carries the bearer token and, when <see cref="Tenant"/> is set,
/// the <c>X-Tenant</c> header, like the Vue app does.
/// </summary>
internal sealed class Session(HttpClient client, string accessToken, string email)
{
    public string Email { get; } = email;
    public string? Tenant { get; set; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<Session> SignUp(ApiFactory api, string? email = null)
    {
        var client = api.CreateBareClient();
        email ??= Http.NewEmail();
        var register = await client.Register(email);
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        return new Session(client, (await register.Json()).GetProperty("accessToken").GetString()!, email);
    }

    /// <summary>A new user made platform admin in the database, then signed in again so the token carries <c>padm</c>.</summary>
    public static async Task<Session> PlatformAdmin(IntegrationFixture fixture)
    {
        var email = Http.NewEmail();
        await SignUp(fixture.Factory, email);
        await fixture.Sql("UPDATE identity.users SET is_platform_admin = true WHERE email = @p0", email);

        var client = fixture.Factory.CreateBareClient();
        var login = await client.Login(email);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        return new Session(client, (await login.Json()).GetProperty("accessToken").GetString()!, email);
    }

    /// <summary>Signs up a new user and creates a business they own; <see cref="Tenant"/> is set to it.</summary>
    public static async Task<(Session Owner, JsonElement Tenant)> OwnerOfNewTenant(ApiFactory api, string? name = null)
    {
        var owner = await SignUp(api);
        var slug = NewSlug();
        var created = await owner.Post("/api/v1/tenants", new { name = name ?? "Fit With Ahmed", slug });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        owner.Tenant = slug;
        return (owner, await created.Json());
    }

    public static string NewSlug(string prefix = "t") => $"{prefix}-{Guid.CreateVersion7():N}"[..30];

    public Task<HttpResponseMessage> Get(string url) => Send(HttpMethod.Get, url, null);
    public Task<HttpResponseMessage> Post(string url, object? body = null) => Send(HttpMethod.Post, url, body);
    public Task<HttpResponseMessage> Put(string url, object body) => Send(HttpMethod.Put, url, body);
    public Task<HttpResponseMessage> Patch(string url, object body) => Send(HttpMethod.Patch, url, body);
    public Task<HttpResponseMessage> Delete(string url) => Send(HttpMethod.Delete, url, null);

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (Tenant is not null)
            request.Headers.Add("X-Tenant", Tenant);
        return client.SendAsync(request, Ct);
    }
}

internal static class FixtureSql
{
    /// <summary>Runs SQL as <c>app_migrator</c> (table owner, so RLS doesn't apply) with positional parameters @p0, @p1...</summary>
    public static async Task Sql(this IntegrationFixture fixture, string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        for (var i = 0; i < parameters.Length; i++)
            command.Parameters.AddWithValue($"p{i}", parameters[i]);
        await command.ExecuteNonQueryAsync();
    }
}

internal static class JsonExtensions
{
    public static string Str(this JsonElement e, string property) => e.GetProperty(property).GetString()!;

    public static JsonElement[] Items(this JsonElement page) => [.. page.GetProperty("items").EnumerateArray()];

    public static string[] Errors(this JsonElement problem, string field) =>
        [.. problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!)];
}
