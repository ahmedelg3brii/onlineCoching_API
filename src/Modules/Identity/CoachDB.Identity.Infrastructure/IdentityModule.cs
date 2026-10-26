using CoachDB.Identity.Application.Abstractions;
using CoachDB.Identity.Application.Auth;
using CoachDB.Identity.Contracts;
using CoachDB.Identity.Infrastructure.Persistence;
using CoachDB.Identity.Infrastructure.Security;
using CoachDB.Infrastructure.Common;
using CoachDB.Infrastructure.Common.Security;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CoachDB.Identity.Infrastructure;

public static class IdentityModule
{
    /// <summary>Everything the Identity module needs except its endpoints (see <c>AddIdentityEndpoints</c>).</summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>(configuration, IdentityDbContext.SchemaName);
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.Section));

        services.AddValidatorsFromAssemblyContaining<AuthResult>(ServiceLifetime.Scoped, includeInternalTypes: true);

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<SessionIssuer>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();

        return services;
    }
}
