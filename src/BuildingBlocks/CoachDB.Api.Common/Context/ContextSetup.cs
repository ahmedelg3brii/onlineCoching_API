using CoachDB.Application.Abstractions.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoachDB.Api.Common.Context;

public static class ContextSetup
{
    public static IServiceCollection AddRequestContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IUserContext, HttpUserContext>();
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddOptions<TenancyOptions>().BindConfiguration(TenancyOptions.Section);
        return services;
    }
}
