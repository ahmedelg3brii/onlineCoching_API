using CoachDB.Api.Common.Endpoints;
using CoachDB.Api.Common.Results;
using CoachDB.Identity.Api.Auth;
using CoachDB.Identity.Application.Users;
using CoachDB.Identity.Application.Users.GetCurrentUser;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CoachDB.Identity.Api.Me;

/// <summary><c>GET /me</c> → 200 <see cref="UserResponse"/>; 401 <c>auth_required</c> without a valid access token.</summary>
internal sealed class GetMeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/me", async (ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetCurrentUserQuery(), ct)).ToOk())
            .WithName("GetMe")
            .WithTags(Tags.Me)
            .RequireAuthorization()
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
}
