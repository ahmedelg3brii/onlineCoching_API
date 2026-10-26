using CoachDB.Api.Common;
using CoachDB.Api.Common.Context;
using CoachDB.Api.Common.Results;
using CoachDB.Api.Setup;
using CoachDB.Application.Abstractions.Behaviors;
using CoachDB.Identity.Api;
using CoachDB.Identity.Infrastructure;
using CoachDB.Infrastructure.Common;
using CoachDB.Infrastructure.Common.Persistence;
using CoachDB.Tenants.Api;
using CoachDB.Tenants.Infrastructure;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// ---- Request/response contract (BuildingBlocks/CoachDB.Api.Common) ----
builder.Services
    .AddCoachDbProblemDetails()
    .AddCoachDbJson()
    .AddCoachDbApiVersioning()
    .AddRequestContext();

// ---- Use-case pipeline: Logging â†’ Validation â†’ Authorization â†’ UnitOfWork (commands only) â†’ handler ----
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.PipelineBehaviors =
    [
        typeof(LoggingBehavior<,>),
        typeof(ValidationBehavior<,>),
        typeof(AuthorizationBehavior<,>),
        typeof(UnitOfWorkBehavior<,>),
    ];
});

// ---- Persistence: module DbContexts, tenant filters + RLS, outbox dispatcher ----
builder.Services.AddCoachDbInfrastructure();

// ---- Platform services ----
builder.Services
    .AddCoachDbSecurity(builder.Configuration)
    .AddCoachDbRateLimiting(builder.Configuration)
    .AddCoachDbCors(builder.Configuration)
    .AddOpenApi("v1", options => options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "CoachDB API";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    }));
builder.Services.AddHealthChecks();

// ---- Modules: one line each, in dependency order ----
builder.Services.AddIdentityModule(builder.Configuration).AddIdentityEndpoints();
builder.Services.AddTenantsModule(builder.Configuration).AddTenantsEndpoints();

var app = builder.Build();

// Middleware order matters (see Architecture Plan â†’ Request pipeline).
app.UseExceptionHandler();
app.UseStatusCodePages();            // empty 404/405/401 bodies â†’ problem details
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseCors(SecuritySetup.CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();    // X-Tenant / subdomain → ITenantContext + member permissions
app.UseAuthorization();

if (!app.Environment.IsProduction())
{
    app.MapOpenApi();
    app.MapScalarApiReference();     // /scalar
}

app.MapCoachDbHealthChecks();
app.MapApiV1().RequireRateLimiting(RateLimitPolicies.Api);

app.Run();

/// <summary>Entry point; public so API tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
