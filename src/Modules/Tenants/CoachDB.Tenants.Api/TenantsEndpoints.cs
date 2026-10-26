using System.Text.Json;
using CoachDB.Api.Common.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace CoachDB.Tenants.Api;

public static class TenantsEndpoints
{
    /// <summary>Registers this module's <see cref="IEndpoint"/>s; the host maps them under <c>/api/v1</c>.</summary>
    public static IServiceCollection AddTenantsEndpoints(this IServiceCollection services) =>
        services.AddEndpoints(typeof(TenantsEndpoints).Assembly);
}

internal static class Tags
{
    public const string Tenants = "Tenants";
    public const string Plans = "Plans";
    public const string PaymentAccounts = "Payment accounts";
    public const string Admin = "Admin";
}

/// <summary>Query-string enums use the same snake_case spelling as JSON (<c>?status=past_due</c>).</summary>
internal static class SnakeCaseEnum
{
    public static bool TryParse<TEnum>(string? value, out TEnum? result)
        where TEnum : struct, Enum
    {
        result = null;
        if (string.IsNullOrEmpty(value))
            return true;

        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (JsonNamingPolicy.SnakeCaseLower.ConvertName(candidate.ToString()) == value)
            {
                result = candidate;
                return true;
            }
        }

        return false;
    }
}
