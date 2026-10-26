using CoachDB.Api.Common.Context;
using CoachDB.Api.Common.Endpoints;
using CoachDB.Api.Common.Results;
using CoachDB.Application.Abstractions.Paging;
using CoachDB.Tenants.Application.PaymentAccounts;
using CoachDB.Tenants.Application.Plans;
using CoachDB.Tenants.Domain.Billing;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CoachDB.Tenants.Api.Billing;

/// <param name="ProviderAccountId">The provider's public merchant/account id. Never a secret key.</param>
public sealed record ConnectPaymentAccountRequest(PaymentProvider Provider, string? ProviderAccountId, bool MakeDefault = false);

/// <summary><c>GET /plans</c> → 200 <c>{ items: PlanResponse[] }</c>: the SaaS price list, cheapest first. Anonymous.</summary>
internal sealed class ListPlansEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/plans", async (ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListPlansQuery(), ct)).ToOk())
            .WithName("ListPlans")
            .WithTags(Tags.Plans)
            .AllowAnonymous()
            .WithoutTenant()
            .Produces<CursorPage<PlanResponse>>();
}

/// <summary><c>GET /tenant/plan</c> → 200 <see cref="TenantPlanResponse"/>: the business's plan, subscription and usage. Any member.</summary>
internal sealed class GetTenantPlanEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/tenant/plan", async (ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetTenantPlanQuery(), ct)).ToOk())
            .WithName("GetTenantPlan")
            .WithTags(Tags.Plans)
            .RequireAuthorization()
            .Produces<TenantPlanResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}

/// <summary>
/// Payout accounts of the current business (owner, <c>tenant.billing</c>):
/// <c>GET /tenant/payment-accounts</c>, <c>POST</c> (201), <c>GET /{id}</c>, <c>POST /{id}/default</c>, <c>DELETE /{id}</c> (204).
/// Another business's id is 404 <c>payment_account_not_found</c>.
/// </summary>
internal sealed class PaymentAccountEndpoints : IEndpoint
{
    private const string Route = "/tenant/payment-accounts";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .WithTags(Tags.PaymentAccounts)
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("", async (ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListPaymentAccountsQuery(), ct)).ToOk())
            .WithName("ListPaymentAccounts")
            .Produces<CursorPage<PaymentAccountResponse>>();

        group.MapPost("", async (ConnectPaymentAccountRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new ConnectPaymentAccountCommand(request.Provider, request.ProviderAccountId, request.MakeDefault), ct);
                return result.Match(account => TypedResults.Created($"/api/v1{Route}/{account.Id}", account));
            })
            .WithName("ConnectPaymentAccount")
            .Produces<PaymentAccountResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetPaymentAccountQuery(id), ct)).ToOk())
            .WithName("GetPaymentAccount")
            .Produces<PaymentAccountResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/default", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new MakeDefaultPaymentAccountCommand(id), ct)).ToOk())
            .WithName("MakeDefaultPaymentAccount")
            .Produces<PaymentAccountResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RemovePaymentAccountCommand(id), ct)).ToNoContent())
            .WithName("RemovePaymentAccount")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
