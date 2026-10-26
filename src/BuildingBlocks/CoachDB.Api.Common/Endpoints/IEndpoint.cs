using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoachDB.Api.Common.Endpoints;

/// <summary>
/// One HTTP route. Each feature has one endpoint class that binds the request, sends a command or
/// query, and maps the <c>Result</c> to HTTP — nothing else.
/// </summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

public static class EndpointRegistration
{
    /// <summary>Registers every non-abstract <see cref="IEndpoint"/> in <paramref name="assembly"/>. Call once per module Api assembly.</summary>
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        var descriptors = assembly.DefinedTypes
            .Where(t => t is { IsAbstract: false, IsInterface: false } && t.IsAssignableTo(typeof(IEndpoint)))
            .Select(t => ServiceDescriptor.Transient(typeof(IEndpoint), t));

        services.TryAddEnumerable(descriptors);
        return services;
    }

    /// <summary>Maps all registered endpoints under <paramref name="group"/> (e.g. the <c>/api/v1</c> group).</summary>
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder group)
    {
        foreach (var endpoint in group.ServiceProvider.GetServices<IEndpoint>())
            endpoint.MapEndpoint(group);

        return group;
    }
}
