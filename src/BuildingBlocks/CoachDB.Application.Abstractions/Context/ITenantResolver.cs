using CoachDB.SharedKernel.Results;

namespace CoachDB.Application.Abstractions.Context;

/// <summary>
/// Turns the tenant a request names (slug from the subdomain or <c>X-Tenant</c>) into a tenant id and the caller's
/// permissions in it. Implemented by the Tenants module; called by the host's tenant resolution middleware
/// before any handler runs.
/// </summary>
public interface ITenantResolver
{
    /// <param name="slug">Lower-case slug as sent by the client.</param>
    /// <param name="userId">The signed-in user, or null for an anonymous request.</param>
    /// <returns>
    /// The resolved tenant, or <see cref="TenancyErrors.TenantNotFound"/>, <see cref="TenancyErrors.TenantSuspended"/>,
    /// <see cref="TenancyErrors.TenantAccessDenied"/> (signed in but not an active member).
    /// </returns>
    Task<Result<ResolvedTenant>> ResolveAsync(string slug, Guid? userId, CancellationToken cancellationToken);
}

/// <param name="Role">The member's role (e.g. <c>owner</c>), or null for an anonymous request.</param>
/// <param name="Permissions">What the caller may do in this tenant; empty for an anonymous request.</param>
public sealed record ResolvedTenant(Guid TenantId, string Slug, string? Role, IReadOnlyCollection<string> Permissions);

/// <summary>Errors of tenant resolution and tenant-scoped authorization, shared by every module.</summary>
public static class TenancyErrors
{
    public static readonly Error TenantRequired =
        Error.Validation("tenant_required", "Choose a business: send the X-Tenant header with its slug.");

    public static readonly Error TenantNotFound =
        Error.NotFound("tenant_not_found", "The business was not found.");

    public static readonly Error TenantSuspended =
        Error.Forbidden("tenant_suspended", "This business is suspended.");

    public static readonly Error TenantAccessDenied =
        Error.Forbidden("tenant_access_denied", "You are not a member of this business.");
}
