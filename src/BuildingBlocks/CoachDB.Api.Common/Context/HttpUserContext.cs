using System.Security.Claims;
using CoachDB.Application.Abstractions.Context;
using Microsoft.AspNetCore.Http;

namespace CoachDB.Api.Common.Context;

/// <summary>Reads the signed-in user from the JWT. Permissions are loaded per tenant by tenant resolution.</summary>
internal sealed class HttpUserContext(IHttpContextAccessor accessor) : IUserContext
{
    public const string UserIdClaim = "sub";
    public const string PlatformAdminClaim = "padm";
    public const string PermissionClaim = "perm";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId =>
        Guid.TryParse(Principal?.FindFirstValue(UserIdClaim) ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("No authenticated user on this request.");

    public bool IsPlatformAdmin => Principal?.HasClaim(PlatformAdminClaim, "true") == true;

    public bool HasPermission(string permission) =>
        Principal?.HasClaim(PermissionClaim, permission) == true;
}
