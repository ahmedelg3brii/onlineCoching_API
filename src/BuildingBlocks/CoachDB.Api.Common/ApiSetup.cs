using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Asp.Versioning.Builder;
using CoachDB.Api.Common.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CoachDB.Api.Common;

public static class ApiSetup
{
    /// <summary>
    /// JSON conventions for every request and response: camelCase properties, snake_case enum strings
    /// (<c>"pending_payment"</c>), ISO-8601 dates; unknown enum values and trailing commas are rejected.
    /// </summary>
    public static IServiceCollection AddCoachDbJson(this IServiceCollection services) =>
        services.Configure<JsonOptions>(options => ConfigureJson(options.SerializerOptions));

    public static void ConfigureJson(JsonSerializerOptions json)
    {
        json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        json.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        json.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        json.AllowTrailingCommas = false;
    }

    public static IServiceCollection AddCoachDbApiVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1);
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            // OpenAPI shows /api/v1/... (not /api/v{version}/...), one document per version ("v1", later "v2").
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });
        return services;
    }

    /// <summary>Creates the <c>/api/v1</c> group and maps every registered <see cref="IEndpoint"/> into it.</summary>
    public static RouteGroupBuilder MapApiV1(this WebApplication app)
    {
        ApiVersionSet versions = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1)).Build();

        var v1 = app.MapGroup("/api/v{version:apiVersion}").WithApiVersionSet(versions);
        v1.MapEndpoints();
        return v1;
    }
}
