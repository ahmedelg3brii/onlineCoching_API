namespace CoachDB.Application.Abstractions.Context;

/// <summary>The tenant (coaching business) the current request or job runs for. Set by tenant resolution, read everywhere.</summary>
public interface ITenantContext
{
    bool HasTenant { get; }

    /// <summary>Throws when no tenant is resolved; use <see cref="HasTenant"/> on tenant-less routes.</summary>
    Guid TenantId { get; }

    string? TenantSlug { get; }
}

/// <summary>The signed-in user and the permissions they hold in the current tenant.</summary>
public interface IUserContext
{
    bool IsAuthenticated { get; }

    /// <summary>Throws when the request is anonymous.</summary>
    Guid UserId { get; }

    bool IsPlatformAdmin { get; }

    bool HasPermission(string permission);
}
