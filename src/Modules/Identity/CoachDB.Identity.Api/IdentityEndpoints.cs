using CoachDB.Api.Common.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace CoachDB.Identity.Api;

public static class IdentityEndpoints
{
    /// <summary>Registers this module's <see cref="IEndpoint"/>s; the host maps them under <c>/api/v1</c>.</summary>
    public static IServiceCollection AddIdentityEndpoints(this IServiceCollection services) =>
        services.AddEndpoints(typeof(IdentityEndpoints).Assembly);
}
