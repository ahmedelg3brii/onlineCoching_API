using System.Net;
using System.Text.Json;

namespace CoachDB.Api.Tests;

public sealed class PipelineTests(CoachDbApiFactory factory) : IClassFixture<CoachDbApiFactory>
{
    private const string CorrelationHeader = "X-Correlation-Id";

    [Fact]
    public async Task Live_health_check_returns_200()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details_with_code_and_trace_id()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/x", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = body.RootElement;
        root.GetProperty("status").GetInt32().ShouldBe(404);
        root.GetProperty("code").GetString().ShouldBe("not_found");
        root.GetProperty("type").GetString().ShouldBe("https://coachdb.app/errors/not_found");

        var correlationId = response.Headers.GetValues(CorrelationHeader).Single();
        root.GetProperty("traceId").GetString().ShouldBe(correlationId);
    }

    [Fact]
    public async Task Client_correlation_id_is_echoed_and_used_as_trace_id()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/x");
        request.Headers.Add(CorrelationHeader, "demo-123");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues(CorrelationHeader).Single().ShouldBe("demo-123");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("traceId").GetString().ShouldBe("demo-123");
    }

    [Fact]
    public async Task Invalid_client_correlation_id_is_replaced()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(CorrelationHeader, "bad id with spaces");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var echoed = response.Headers.GetValues(CorrelationHeader).Single();
        echoed.ShouldNotBe("bad id with spaces");
        echoed.ShouldNotBeNullOrWhiteSpace();
    }
}
