using CoachDB.Api.Common;
using CoachDB.Api.Common.Endpoints;
using CoachDB.Api.Common.Results;
using CoachDB.Identity.Application.Auth.Login;
using CoachDB.Identity.Application.Auth.Logout;
using CoachDB.Identity.Application.Auth.Refresh;
using CoachDB.Identity.Application.Auth.Register;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CoachDB.Identity.Api.Auth;

internal static class Tags
{
    public const string Auth = "Auth";
    public const string Me = "Me";
}

/// <summary><c>POST /auth/register</c> â†’ 201 + Location <c>/api/v1/me</c>, body <see cref="AuthResponse"/>, refresh cookie set.</summary>
internal sealed class RegisterEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/register", async (RegisterRequest request, ISender sender, HttpResponse response, TimeProvider clock, CancellationToken ct) =>
            {
                var result = await sender.Send(new RegisterCommand(request.Email, request.Password, request.FullName, request.Locale), ct);
                return result.Match(auth =>
                {
                    RefreshCookie.Set(response, auth);
                    return TypedResults.Created("/api/v1/me", AuthResponse.From(auth, clock.GetUtcNow()));
                });
            })
            .WithName("Register")
            .WithTags(Tags.Auth)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
}

/// <summary><c>POST /auth/login</c> â†’ 200 <see cref="AuthResponse"/>, or 401 <c>invalid_credentials</c>.</summary>
internal sealed class LoginEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/login", async (LoginRequest request, ISender sender, HttpResponse response, TimeProvider clock, CancellationToken ct) =>
            {
                var result = await sender.Send(new LoginCommand(request.Email, request.Password), ct);
                return result.Match(auth =>
                {
                    RefreshCookie.Set(response, auth);
                    return TypedResults.Ok(AuthResponse.From(auth, clock.GetUtcNow()));
                });
            })
            .WithName("Login")
            .WithTags(Tags.Auth)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
}

/// <summary>
/// <c>POST /auth/refresh</c> â†’ 200 with a new access token and a rotated refresh cookie.
/// 401 <c>refresh_token_invalid</c> or <c>refresh_token_reused</c> clears the cookie.
/// </summary>
internal sealed class RefreshEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/refresh", async Task<IResult> (HttpRequest request, ISender sender, TimeProvider clock, CancellationToken ct) =>
            {
                var body = request.HasJsonContentType() ? await request.ReadFromJsonAsync<RefreshRequest>(ct) : null;
                var result = await sender.Send(new RefreshSessionCommand(RefreshCookie.Read(request, body)), ct);

                if (result.IsFailure)
                {
                    RefreshCookie.Clear(request.HttpContext.Response);
                    return result.Error.ToProblem();
                }

                RefreshCookie.Set(request.HttpContext.Response, result.Value);
                return TypedResults.Ok(AuthResponse.From(result.Value, clock.GetUtcNow()));
            })
            .WithName("RefreshSession")
            .WithTags(Tags.Auth)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .Accepts<RefreshRequest>(isOptional: true, "application/json")
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
}

/// <summary><c>POST /auth/logout</c> â†’ 204. Ends the session and clears the cookie; safe to call twice.</summary>
internal sealed class LogoutEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/logout", async (HttpRequest request, ISender sender, CancellationToken ct) =>
            {
                var body = request.HasJsonContentType() ? await request.ReadFromJsonAsync<RefreshRequest>(ct) : null;
                var result = await sender.Send(new LogoutCommand(RefreshCookie.Read(request, body)), ct);
                RefreshCookie.Clear(request.HttpContext.Response);
                return result.ToNoContent();
            })
            .WithName("Logout")
            .WithTags(Tags.Auth)
            .AllowAnonymous()
            .Accepts<RefreshRequest>(isOptional: true, "application/json")
            .Produces(StatusCodes.Status204NoContent);
}
