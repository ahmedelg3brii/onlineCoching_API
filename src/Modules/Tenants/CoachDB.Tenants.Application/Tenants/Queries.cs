using CoachDB.Application.Abstractions.Authorization;
using CoachDB.Application.Abstractions.Behaviors;
using CoachDB.Application.Abstractions.Context;
using CoachDB.Application.Abstractions.Messaging;
using CoachDB.Application.Abstractions.Paging;
using CoachDB.SharedKernel.Results;
using CoachDB.Tenants.Application.Abstractions;
using CoachDB.Tenants.Application.Authorization;
using CoachDB.Tenants.Domain;
using CoachDB.Tenants.Domain.Tenants;

namespace CoachDB.Tenants.Application.Tenants;

/// <summary>The business chosen by <c>X-Tenant</c> (<c>GET /tenant</c>). Any member may read it.</summary>
[RequiresTenant]
public sealed record GetCurrentTenantQuery : IQuery<TenantResponse>;

internal sealed class GetCurrentTenantHandler(ITenantContext tenant, ITenantRepository tenants)
    : IQueryHandler<GetCurrentTenantQuery, TenantResponse>
{
    public async ValueTask<Result<TenantResponse>> Handle(GetCurrentTenantQuery query, CancellationToken cancellationToken)
    {
        var found = await tenants.GetByIdAsync(tenant.TenantId, cancellationToken);
        return found is null ? TenantsErrors.TenantNotFound : TenantResponse.From(found);
    }
}

/// <summary>A business's public profile by slug (<c>GET /public/{slug}</c>), for its landing page. Anonymous.</summary>
public sealed record GetPublicTenantQuery(string Slug) : IQuery<PublicTenantResponse>;

internal sealed class GetPublicTenantHandler(ITenantRepository tenants) : IQueryHandler<GetPublicTenantQuery, PublicTenantResponse>
{
    public async ValueTask<Result<PublicTenantResponse>> Handle(GetPublicTenantQuery query, CancellationToken cancellationToken)
    {
        var slug = Slug.Create(query.Slug);
        if (slug.IsFailure)
            return TenantsErrors.TenantNotFound;

        var tenant = await tenants.GetBySlugAsync(slug.Value.Value, cancellationToken);
        return tenant is { IsAccessible: true } ? PublicTenantResponse.From(tenant) : TenantsErrors.TenantNotFound;
    }
}

/// <summary>Whether a slug can be used for a new business, and if not, why (<c>slug_invalid</c>, <c>slug_reserved</c>, <c>slug_taken</c>).</summary>
public sealed record CheckSlugQuery(string? Slug) : IQuery<SlugAvailabilityResponse>;

public sealed record SlugAvailabilityResponse(string Slug, bool Available, string? Reason);

internal sealed class CheckSlugHandler(ITenantRepository tenants) : IQueryHandler<CheckSlugQuery, SlugAvailabilityResponse>
{
    public async ValueTask<Result<SlugAvailabilityResponse>> Handle(CheckSlugQuery query, CancellationToken cancellationToken)
    {
        var normalized = query.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        var slug = Slug.Create(normalized);
        if (slug.IsFailure)
            return new SlugAvailabilityResponse(normalized, false, slug.Error.Code);

        return await tenants.SlugExistsAsync(normalized, cancellationToken)
            ? new SlugAvailabilityResponse(normalized, false, TenantsErrors.SlugTaken.Code)
            : new SlugAvailabilityResponse(normalized, true, null);
    }
}

/// <summary>Every business the signed-in user is an active member of (<c>GET /me/tenants</c>). Never paged: a handful at most.</summary>
public sealed record ListMyTenantsQuery : IQuery<CursorPage<MyTenantResponse>>;

internal sealed class ListMyTenantsHandler(IUserContext user, ITenantMemberRepository members)
    : IQueryHandler<ListMyTenantsQuery, CursorPage<MyTenantResponse>>
{
    public async ValueTask<Result<CursorPage<MyTenantResponse>>> Handle(ListMyTenantsQuery query, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated)
            return AuthorizationBehavior<ListMyTenantsQuery, Result<CursorPage<MyTenantResponse>>>.AuthRequired;

        var memberships = await members.ListForUserAsync(user.UserId, cancellationToken);
        var items = memberships
            .Select(m => new MyTenantResponse(m.Tenant.Id, m.Tenant.Slug, m.Tenant.Name, m.Tenant.Status, m.Role,
                RolePermissions.For(m.Role), m.Tenant.Branding.LogoUrl))
            .ToList();
        return new CursorPage<MyTenantResponse>(items, null, false);
    }
}
