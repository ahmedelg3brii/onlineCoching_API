using System.Security.Claims;
using CoachDB.Api.Common.Results;
using CoachDB.Application.Abstractions.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoachDB.Api.Common.Context;

/// <summary>Configuration section <c>Tenancy</c>.</summary>
public sealed class TenancyOptions
{
    public const string Section = "Tenancy";

    /// <summary>
    /// The platform's domain, e.g. <c>coachdb.app</c>: a request to <c>fitwithahmed.coachdb.app</c> resolves the tenant
    /// <c>fitwithahmed</c>. Leave empty to resolve only from the <c>X-Tenant</c> header (local development).
    /// </summary>
    public string? RootDomain { get; set; }

    /// <summary>Subdomains of <see cref="RootDomain"/> that are the platform's own, never a tenant.</summary>
    public string[] PlatformSubdomains { get; set; } = ["www", "api", "app", "admin"];
}

/// <summary>Endpoint metadata: tenant resolution skips this endpoint even when <c>X-Tenant</c> is sent.</summary>
public sealed class TenantAgnosticMetadata
{
    public static readonly TenantAgnosticMetadata Instance = new();

    private TenantAgnosticMetadata() { }
}

public static class TenantEndpointConventions
{
    /// <summary>
    /// Marks an endpoint that runs outside any tenant (sign-in, creating a business, the public site, <c>/admin</c>).
    /// A frontend that always sends <c>X-Tenant</c> can still call it.
    /// </summary>
    public static TBuilder WithoutTenant<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(TenantAgnosticMetadata.Instance);
}

/// <summary>
/// Pipeline step after authentication: finds the tenant the request names (subdomain of
/// <see cref="TenancyOptions.RootDomain"/>, then the <c>X-Tenant</c> header), checks that the signed-in user is a
/// member, then sets <see cref="TenantContext"/> and adds the member's permissions to the user as <c>perm</c> claims.
/// A request that names no tenant passes through untouched; tenant-scoped handlers then answer <c>tenant_required</c>.
/// Unknown slug → 404 <c>tenant_not_found</c>; not a member → 403 <c>tenant_access_denied</c>.
/// </summary>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    IOptions<TenancyOptions> options,
    ILogger<TenantResolutionMiddleware> logger)
{
    public const string HeaderName = "X-Tenant";
    public const string RoleClaim = "tenant_role";
    private const int MaxSlugLength = 63;

    public async Task InvokeAsync(HttpContext context, TenantContext tenant, IUserContext user)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<TenantAgnosticMetadata>() is not null
            || SlugFrom(context.Request) is not { } slug)
        {
            await next(context);
            return;
        }

        var resolver = context.RequestServices.GetRequiredService<ITenantResolver>();
        var resolved = IsWellFormed(slug)
            ? await resolver.ResolveAsync(slug, user.IsAuthenticated ? user.UserId : null, context.RequestAborted)
            : TenancyErrors.TenantNotFound;

        if (resolved.IsFailure)
        {
            await resolved.Error.ToProblem().ExecuteAsync(context);
            return;
        }

        var value = resolved.Value;
        tenant.Set(value.TenantId, value.Slug);
        if (value.Role is not null)
        {
            var claims = value.Permissions.Select(p => new Claim(HttpUserContext.PermissionClaim, p))
                .Append(new Claim(RoleClaim, value.Role));
            context.User.AddIdentity(new ClaimsIdentity(claims));
        }

        using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = value.TenantId }))
        {
            await next(context);
        }
    }

    private string? SlugFrom(HttpRequest request)
    {
        var root = options.Value.RootDomain;
        if (!string.IsNullOrEmpty(root))
        {
            var host = request.Host.Host;
            var suffix = "." + root;
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var sub = host[..^suffix.Length];
                if (sub.Length > 0 && !sub.Contains('.')
                    && !options.Value.PlatformSubdomains.Contains(sub, StringComparer.OrdinalIgnoreCase))
                    return sub.ToLowerInvariant();
            }
        }

        var header = request.Headers[HeaderName].ToString().Trim();
        return header.Length > 0 ? header.ToLowerInvariant() : null;
    }

    // Cheap pre-check so junk never reaches the database; the Tenants module owns the full slug rules.
    private static bool IsWellFormed(string slug) =>
        slug.Length <= MaxSlugLength && slug.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}
