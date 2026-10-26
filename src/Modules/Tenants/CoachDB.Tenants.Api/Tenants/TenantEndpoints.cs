using CoachDB.Api.Common.Context;
using CoachDB.Api.Common.Endpoints;
using CoachDB.Api.Common.Results;
using CoachDB.Application.Abstractions.Paging;
using CoachDB.Tenants.Application.Tenants;
using CoachDB.Tenants.Application.Tenants.Create;
using CoachDB.Tenants.Application.Tenants.Update;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CoachDB.Tenants.Api.Tenants;

// HTTP request bodies. They are separate from commands so the wire format can stay stable while use cases change.

public sealed record CreateTenantRequest(string Name, string Slug, string? DefaultCurrency, string? Timezone, string? DefaultLocale);

/// <summary>Partial update: leave a field out to keep it. <c>branding</c> and <c>settings</c> replace the whole object.</summary>
public sealed record UpdateTenantRequest(
    string? Name,
    string? DefaultCurrency,
    string? Timezone,
    string? DefaultLocale,
    TenantBrandingDto? Branding,
    TenantSettingsInput? Settings);

/// <summary>
/// <c>POST /tenants</c> → 201 + Location <c>/api/v1/tenant</c>, body <see cref="TenantResponse"/>. The caller becomes the
/// owner and the business starts its trial. 409 <c>slug_taken</c>; 403 <c>tenant_limit_reached</c>.
/// </summary>
internal sealed class CreateTenantEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/tenants", async (CreateTenantRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new CreateTenantCommand(
                    request.Name, request.Slug, request.DefaultCurrency, request.Timezone, request.DefaultLocale), ct);
                return result.Match(tenant => TypedResults.Created("/api/v1/tenant", tenant));
            })
            .WithName("CreateTenant")
            .WithTags(Tags.Tenants)
            .RequireAuthorization()
            .WithoutTenant()
            .Produces<TenantResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
}

/// <summary><c>GET /tenants/slug-availability?slug=</c> → 200 <see cref="SlugAvailabilityResponse"/>. For the sign-up form; anonymous.</summary>
internal sealed class CheckSlugEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/tenants/slug-availability", async (string? slug, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CheckSlugQuery(slug), ct)).ToOk())
            .WithName("CheckSlugAvailability")
            .WithTags(Tags.Tenants)
            .AllowAnonymous()
            .WithoutTenant()
            .Produces<SlugAvailabilityResponse>();
}

/// <summary>
/// <c>GET /me/tenants</c> → 200 <c>{ items: MyTenantResponse[], nextCursor: null, hasMore: false }</c>: the businesses the
/// user works in, with role and permissions. Send an item's <c>slug</c> as <c>X-Tenant</c> to work in it.
/// </summary>
internal sealed class ListMyTenantsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/me/tenants", async (ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListMyTenantsQuery(), ct)).ToOk())
            .WithName("ListMyTenants")
            .WithTags(Tags.Tenants)
            .RequireAuthorization()
            .WithoutTenant()
            .Produces<CursorPage<MyTenantResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
}

/// <summary><c>GET /public/{slug}</c> → 200 <see cref="PublicTenantResponse"/>, or 404 <c>tenant_not_found</c> (also when suspended). Anonymous.</summary>
internal sealed class GetPublicTenantEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/public/{slug}", async (string slug, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetPublicTenantQuery(slug), ct)).ToOk())
            .WithName("GetPublicTenant")
            .WithTags(Tags.Tenants)
            .AllowAnonymous()
            .WithoutTenant()
            .Produces<PublicTenantResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary><c>GET /tenant</c> (with <c>X-Tenant</c>) → 200 <see cref="TenantResponse"/>. Any member.</summary>
internal sealed class GetCurrentTenantEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/tenant", async (ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetCurrentTenantQuery(), ct)).ToOk())
            .WithName("GetCurrentTenant")
            .WithTags(Tags.Tenants)
            .RequireAuthorization()
            .Produces<TenantResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary><c>PATCH /tenant</c> → 200 <see cref="TenantResponse"/>. Needs <c>tenant.manage</c> (owner).</summary>
internal sealed class UpdateTenantEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/tenant", async (UpdateTenantRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateTenantCommand(
                    request.Name, request.DefaultCurrency, request.Timezone, request.DefaultLocale, request.Branding, request.Settings), ct)).ToOk())
            .WithName("UpdateTenant")
            .WithTags(Tags.Tenants)
            .RequireAuthorization()
            .Produces<TenantResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
