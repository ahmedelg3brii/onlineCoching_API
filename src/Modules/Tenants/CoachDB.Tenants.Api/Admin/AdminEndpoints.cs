using CoachDB.Api.Common.Context;
using CoachDB.Api.Common.Endpoints;
using CoachDB.Api.Common.Paging;
using CoachDB.Api.Common.Results;
using CoachDB.Application.Abstractions.Dtos;
using CoachDB.Application.Abstractions.Paging;
using CoachDB.SharedKernel.Results;
using CoachDB.Tenants.Application.Admin;
using CoachDB.Tenants.Application.Plans;
using CoachDB.Tenants.Application.Tenants;
using CoachDB.Tenants.Domain.Tenants;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CoachDB.Tenants.Api.Admin;

public sealed record CreatePlanRequest(
    string Code,
    string Name,
    MoneyDto MonthlyPrice,
    int? MaxClients,
    int? MaxCoaches,
    IReadOnlyDictionary<string, bool>? Features);

/// <summary>Full replacement (PUT): send every field. <c>null</c> limits mean unlimited.</summary>
public sealed record UpdatePlanRequest(
    string Name,
    MoneyDto MonthlyPrice,
    int? MaxClients,
    int? MaxCoaches,
    IReadOnlyDictionary<string, bool>? Features);

/// <summary>
/// Platform admin SaaS plans (<c>padm</c> claim, else 403 <c>permission_denied</c>):
/// <c>GET /admin/plans</c>, <c>POST</c> (201), <c>GET|PUT /{id}</c>, <c>POST /{id}/activate</c>, <c>POST /{id}/deactivate</c>.
/// </summary>
internal sealed class AdminPlanEndpoints : IEndpoint
{
    private const string Route = "/admin/plans";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .WithTags(Tags.Admin)
            .RequireAuthorization()
            .WithoutTenant()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("", async (ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListAllPlansQuery(), ct)).ToOk())
            .WithName("AdminListPlans")
            .Produces<CursorPage<PlanResponse>>();

        group.MapPost("", async (CreatePlanRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new CreatePlanCommand(
                    request.Code, request.Name, request.MonthlyPrice, request.MaxClients, request.MaxCoaches, request.Features), ct);
                return result.Match(plan => TypedResults.Created($"/api/v1{Route}/{plan.Id}", plan));
            })
            .WithName("AdminCreatePlan")
            .Produces<PlanResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetPlanQuery(id), ct)).ToOk())
            .WithName("AdminGetPlan")
            .Produces<PlanResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (Guid id, UpdatePlanRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdatePlanCommand(
                    id, request.Name, request.MonthlyPrice, request.MaxClients, request.MaxCoaches, request.Features), ct)).ToOk())
            .WithName("AdminUpdatePlan")
            .Produces<PlanResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/activate", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SetPlanActiveCommand(id, true), ct)).ToOk())
            .WithName("AdminActivatePlan")
            .Produces<PlanResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SetPlanActiveCommand(id, false), ct)).ToOk())
            .WithName("AdminDeactivatePlan")
            .Produces<PlanResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}

/// <summary>
/// Platform admin view of businesses: <c>GET /admin/tenants?status=&amp;search=&amp;cursor=&amp;limit=</c> (newest first),
/// <c>GET /{id}</c>, <c>POST /{id}/suspend</c>, <c>POST /{id}/reactivate</c>.
/// </summary>
internal sealed class AdminTenantEndpoints : IEndpoint
{
    private const string Route = "/admin/tenants";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .WithTags(Tags.Admin)
            .RequireAuthorization()
            .WithoutTenant()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("", async Task<IResult> ([AsParameters] PageQuery page, string? status, string? search, ISender sender, CancellationToken ct) =>
            {
                var paging = page.ToPageRequest();
                if (paging.IsFailure)
                    return paging.Error.ToProblem();
                if (!SnakeCaseEnum.TryParse<TenantStatus>(status, out var parsed))
                    return new ValidationError(new Dictionary<string, string[]> { ["status"] = ["invalid_value"] }).ToProblem();

                return (await sender.Send(new ListTenantsQuery(parsed, search, paging.Value), ct)).ToOk();
            })
            .WithName("AdminListTenants")
            .Produces<CursorPage<TenantResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetTenantQuery(id), ct)).ToOk())
            .WithName("AdminGetTenant")
            .Produces<TenantResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/suspend", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SuspendTenantCommand(id), ct)).ToOk())
            .WithName("AdminSuspendTenant")
            .Produces<TenantResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/reactivate", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ReactivateTenantCommand(id), ct)).ToOk())
            .WithName("AdminReactivateTenant")
            .Produces<TenantResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
