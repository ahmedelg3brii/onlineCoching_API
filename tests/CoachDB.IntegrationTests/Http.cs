using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CoachDB.IntegrationTests;

/// <summary>Small helpers so tests read as request → status → body shape.</summary>
internal static class Http
{
    public const string RefreshCookie = "coachdb_rt";

    public static string NewEmail() => $"user-{Guid.CreateVersion7():N}@example.com";

    public static Task<HttpResponseMessage> Register(this HttpClient client, string email, string password = "Secret123!", string fullName = "Test User") =>
        client.PostAsJsonAsync("/api/v1/auth/register", new { email, password, fullName });

    public static Task<HttpResponseMessage> Login(this HttpClient client, string email, string password = "Secret123!") =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    public static Task<HttpResponseMessage> RefreshWithBody(this HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });

    public static Task<HttpResponseMessage> GetMe(this HttpClient client, string? bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client.SendAsync(request);
    }

    public static async Task<JsonElement> Json(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Asserts the RFC 7807 contract: status, problem+json, stable code, type URL, traceId.</summary>
    public static async Task<JsonElement> ShouldBeProblem(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await response.Json();
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("status").GetInt32().ShouldBe((int)status);
        body.GetProperty("type").GetString().ShouldBe($"https://coachdb.app/errors/{code}");
        body.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        return body;
    }

    /// <summary>The raw Set-Cookie header for the refresh cookie, or null.</summary>
    public static string? RefreshSetCookie(this HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(RefreshCookie + "=", StringComparison.Ordinal))
            : null;

    /// <summary>The refresh token value from Set-Cookie (empty when the cookie is being cleared).</summary>
    public static string RefreshToken(this HttpResponseMessage response)
    {
        var header = response.RefreshSetCookie() ?? throw new InvalidOperationException("No refresh cookie in response.");
        return header[(RefreshCookie.Length + 1)..].Split(';')[0];
    }
}
