# CoachDB Backend — Architecture & Implementation Plan

Oct 25, 2026 · Ahmed

## Overview

Build CoachDB as a **modular monolith on .NET 10 LTS**: 12 business modules in one deployable app, each using Clean Architecture inside, vertical slices per feature, and communicating only through public contracts and domain events. This plan turns the [CoachDB Backend PRD](https://claude.ai/artifact/Nu1WFzMSRVAz6aVtFqhUT7) into concrete projects, libraries, patterns and a week-by-week start.

**One change to the PRD:** target **.NET 10 (LTS)**, not .NET 9. [.NET 9 reaches end of support on 10 November 2026](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/); .NET 10 is supported through November 2028. Everything in the PRD (Minimal APIs, EF Core, Npgsql) works the same on .NET 10.

**Key decisions**

| Decision | Choice | Why it serves reliability, maintainability, flexibility |
| --- | --- | --- |
| Runtime | .NET 10 LTS, C# 14, ASP.NET Core Minimal APIs | 3-year support window; one endpoint file per feature |
| Overall style | Modular monolith | One deploy, cheap to run; a module can become a service later |
| Inside a module | Clean Architecture + vertical slices | Rules testable without DB/web; a feature lives in one folder |
| Use cases | CQRS: commands and queries through a mediator | Each use case is one small class with one reason to change |
| Cross-cutting | Pipeline behaviours (decorators) | Validation, logging, transactions, audit written once |
| Module talk | Public contracts + domain/integration events via an outbox | No module reads another's tables; no lost events |
| Isolation | Tenant context + EF global filters + Postgres RLS | Two independent layers stop cross-tenant leaks |
| External services | Interfaces + Strategy/Adapter (payments, storage, notifications) | New provider = one class + config |
| Errors | Result pattern + RFC 7807 problem details with stable codes | Predictable failures the Vue app can act on |
| Quality gates | Architecture tests, isolation suite, contract tests in CI | The rules above are enforced by the build, not by memory |

## Architecture style

Three layers of structure, each solving a different problem: the **modular monolith** keeps business areas apart, **Clean Architecture** keeps business rules apart from frameworks, and **vertical slices** keep each feature in one place.

```text
                 ┌──────────────────────────────────────────┐
                 │  Vue apps: coach dashboard, client PWA   │
                 └────────────────────┬─────────────────────┘
                                      │  REST /api/v1 · SignalR /hubs/chat
                                      ▼
        ┌───────────────────────────────────────────────────────────┐
        │                     CoachDB.Api host                      │
        │  errors · auth · tenant resolution · permissions · limits │
        └─────────────────────────────┬─────────────────────────────┘
                                      ▼
┌──────────────────────────────────────────────────────────────────────────┐
│ Modules (own schema + DbContext each)                                    │
│ Inside each: Api → Application → Domain ← Infrastructure;                │
│ others see only its Contracts                                            │
│                                                                          │
│  ┌──────────────┐   ┌──────────────────┐   ┌────────────────────────────┐│
│  │ Identity     │   │ Tenants          │   │ Team                       ││
│  ├──────────────┤   ├──────────────────┤   ├────────────────────────────┤│
│  │ Clients      │   │ Questionnaires   │   │ Packages & subscriptions   ││
│  ├──────────────┤   ├──────────────────┤   ├────────────────────────────┤│
│  │ Payments     │   │ Progress         │   │ Media                      ││
│  ├──────────────┤   ├──────────────────┤   ├────────────────────────────┤│
│  │ Nutrition    │   │ Training         │   │ Chat & notifications       ││
│  └──────────────┘   └──────────────────┘   └────────────────────────────┘│
└──────────────────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────────────────┐
│ Integration events: outbox table → Hangfire dispatcher → other modules   │
└──────────────────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────────────────┐
│ Building blocks: SharedKernel · Application.Abstractions ·               │
│                  Infrastructure.Common                                   │
└───────┬─────────────────┬──────────────────┬──────────────────┬──────────┘
        ▼                 ▼                  ▼                  ▼
 ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌───────────────────┐
 │PostgreSQL 16 │  │ Redis        │  │ S3 / Blob    │  │ Providers         │
 │RLS per tenant│  │ cache,       │  │ private,     │  │ Paymob, Fawry,    │
 │              │  │ SignalR      │  │ signed URLs  │  │ Stripe, email     │
 └──────────────┘  └──────────────┘  └──────────────┘  └───────────────────┘
```

Requests enter through one host, modules talk only through contracts and outbox events, and every query runs under the tenant's RLS policy.

**1. Modular monolith (the outside).** One ASP.NET Core host loads 12 modules: Identity, Tenants, Team, Clients, Questionnaires, Packages, Payments, Progress, Media, Nutrition, Training, Chat & Notifications. Each module:

- owns its own Postgres schema (`identity`, `payments`, `nutrition`…) and its own `DbContext`;
- exposes only a small `*.Contracts` project (interfaces, DTOs, integration events) to other modules;
- never queries another module's tables, never references another module's internals (enforced by architecture tests).

**2. Clean Architecture (inside each module).** Dependencies point inward only:

1. **Domain** — entities, value objects, domain events, business rules. No EF, no ASP.NET, no NuGet beyond the shared kernel.
2. **Application** — use cases (commands/queries + handlers), validators, interfaces it needs (`IPaymentGateway`, repositories).
3. **Infrastructure** — EF Core `DbContext`, configurations, repositories, provider adapters (Paymob, S3…).
4. **Api (Presentation)** — Minimal API endpoints that map HTTP to commands/queries.

**3. Vertical slices (inside Application + Api).** Each feature is one folder holding its command, handler, validator, response DTO and endpoint, e.g. `Subscriptions/Cancel/`. Adding a feature means adding a folder, not editing five layers of shared services.

**Communication rules**

| From → To | Allowed mechanism | Example |
| --- | --- | --- |
| Vue app → module | HTTP `/api/v1/...` or SignalR `/hubs/chat` | `POST /api/v1/check-ins` |
| Module → module, needs an answer now | Call the other module's public interface from `*.Contracts` (in-process) | Training asks `ISubscriptionAccess.HasFeature(clientId, "training")` |
| Module → module, something happened | Integration event via the outbox | Payments publishes `PaymentSucceeded`; Packages activates the subscription; Chat opens a conversation |
| Inside one module | Domain events dispatched on `SaveChanges` | `CheckIn.Submit()` raises `CheckInSubmitted` |
| Module → outside world | Interface in Application, adapter in Infrastructure | `IPaymentGateway` → `PaymobGateway` |

If a module later needs its own service (Chat is the likely first), the in-process event bus is swapped for a broker (RabbitMQ/Azure Service Bus) and its `Contracts` calls become HTTP/gRPC. No other module changes.

## Solution structure

One Git repo, one `.sln`, five project types per module plus shared building blocks. Projects make the boundaries physical: a module's `Domain` simply cannot reference EF Core because the reference isn't there.

```text
CoachDB/
├─ Directory.Build.props          # net10.0, nullable, warnings-as-errors, analyzers
├─ Directory.Packages.props       # central package versions (one place to upgrade)
├─ .editorconfig
├─ src/
│  ├─ Host/
│  │  └─ CoachDB.Api/             # Program.cs, composition root, middleware, OpenAPI
│  ├─ BuildingBlocks/
│  │  ├─ CoachDB.SharedKernel/    # Entity, AggregateRoot, ValueObject, Result, Error, IDomainEvent
│  │  ├─ CoachDB.Application.Abstractions/  # ICommand, IQuery, handlers, behaviours, ITenantContext, IClock
│  │  └─ CoachDB.Infrastructure.Common/     # base DbContext, tenant interceptor, outbox, Hangfire, Serilog, caching
│  ├─ Modules/
│  │  ├─ Payments/
│  │  │  ├─ CoachDB.Payments.Domain/
│  │  │  ├─ CoachDB.Payments.Application/
│  │  │  ├─ CoachDB.Payments.Infrastructure/
│  │  │  ├─ CoachDB.Payments.Api/          # endpoints only
│  │  │  └─ CoachDB.Payments.Contracts/    # public: interfaces, DTOs, integration events
│  │  ├─ Identity/ Tenants/ Team/ Clients/ Questionnaires/ Packages/
│  │  └─ Progress/ Media/ Nutrition/ Training/ Chat/
├─ tests/
│  ├─ CoachDB.ArchitectureTests/
│  ├─ CoachDB.TenantIsolationTests/
│  ├─ Modules/CoachDB.Payments.UnitTests/ … IntegrationTests/
│  └─ CoachDB.LoadTests/          # k6 scripts
├─ db/                            # the existing tested schema + seed data
├─ deploy/                        # Dockerfile, docker-compose.yml, IaC
└─ .github/workflows/
```

**Inside one feature (vertical slice)**

```text
CoachDB.Packages.Application/Subscriptions/Cancel/
   CancelSubscriptionCommand.cs      # record (SubscriptionId, Reason) : ICommand<Result>
   CancelSubscriptionHandler.cs
   CancelSubscriptionValidator.cs    # FluentValidation
CoachDB.Packages.Api/Subscriptions/
   CancelSubscriptionEndpoint.cs     # POST /subscriptions/{id}/cancel
```

**Dependency rules (checked by architecture tests on every build)**

| Project | May reference | Must never reference |
| --- | --- | --- |
| `*.Domain` | SharedKernel | EF Core, ASP.NET, any other module |
| `*.Application` | its Domain, Application.Abstractions, other modules' `*.Contracts` | Infrastructure, EF Core, other modules' Domain/Application |
| `*.Infrastructure` | its Application + Domain, Infrastructure.Common | other modules' Infrastructure |
| `*.Api` | its Application, Contracts | Infrastructure, Domain types in responses |
| `*.Contracts` | nothing but primitives/SharedKernel | anything else |
| `CoachDB.Api` (host) | every module's `Api` + `Infrastructure` (registration only) | business logic |

Each module exposes one registration entry point, so the host stays small:

```csharp
// Program.cs
builder.Services
    .AddPaymentsModule(builder.Configuration)
    .AddNutritionModule(builder.Configuration);   // … one line per module

app.MapPaymentsEndpoints();
app.MapNutritionEndpoints();
```

## Tech stack and dependencies

Prefer built-in .NET features and permissively licensed (MIT/Apache) libraries; every package version lives in `Directory.Packages.props` so upgrades happen in one file. Pin to the latest stable release when you start.

**Licensing watch-out:** [MediatR and AutoMapper moved to a dual commercial licence](https://www.jimmybogard.com/automapper-and-mediatr-commercial-editions-launch-today/) (free Community edition under $5M annual revenue, but a licence key is still required). This plan uses MIT/Apache alternatives so the codebase never depends on a vendor's pricing. Re-check any library's licence before adding it.

| Concern | Package / tool | Licence | Notes |
| --- | --- | --- | --- |
| Web framework | ASP.NET Core 10 Minimal APIs | MIT | Endpoint groups per module, `TypedResults` |
| API docs | `Microsoft.AspNetCore.OpenApi` + Scalar UI | MIT | OpenAPI file generated on build for the Vue typed client |
| API versioning | `Asp.Versioning.Http` | MIT | `/api/v1`, later `/api/v2` |
| ORM | EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` | MIT / PostgreSQL | One `DbContext` per module, schema per module |
| Naming | `EFCore.NamingConventions` | Apache-2.0 | snake_case to match the existing schema |
| Raw/fast reads | Dapper (optional) | Apache-2.0 | Only for reports/dashboard queries |
| Mediator / CQRS | `Mediator` (martinothamar, source-generated) | MIT | MediatR-style API, no reflection, no licence key |
| Validation | FluentValidation | Apache-2.0 | Runs in a pipeline behaviour |
| Mapping | Mapperly (source-generated) or hand-written | Apache-2.0 | Compile-time, no runtime surprises |
| Auth | ASP.NET Core Identity, JWT Bearer, Google auth | MIT | Access 15 min, refresh 30 days in HttpOnly cookie |
| Real-time | SignalR + `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | MIT | Redis backplane when scaled out |
| Background jobs | Hangfire + `Hangfire.PostgreSql` | LGPL-3.0 / MIT | Free edition is enough; dashboard behind admin auth |
| Outbox / events | Own outbox table + dispatcher (Hangfire recurring job) | — | Avoids a commercial message-bus dependency at this stage |
| Caching | `Microsoft.Extensions.Caching.Hybrid` (HybridCache) + Redis | MIT | L1 memory + L2 Redis, stampede protection |
| Resilience | `Microsoft.Extensions.Http.Resilience` (Polly v8) | MIT / BSD-3 | Retries, timeouts, circuit breaker on Paymob/Fawry/Stripe calls |
| Rate limiting | Built-in `Microsoft.AspNetCore.RateLimiting` | MIT | Login 5/min, API 300/min; partitioned by user/IP |
| Files | `AWSSDK.S3` or `Azure.Storage.Blobs` behind `IFileStorage` | Apache-2.0 / MIT | Pre-signed URLs, 10-minute expiry |
| Images | SixLabors.ImageSharp | Six Labors Split (check revenue terms) | Thumbnails, strip EXIF/GPS; alternative: SkiaSharp (MIT) |
| Payments | Paymob/Fawry via typed `HttpClient`, `Stripe.net` | — / Apache-2.0 | All behind `IPaymentGateway` |
| Email | MailKit (SMTP) or provider SDK (SendGrid/SES) | MIT | Behind `INotificationChannel` |
| Push | `FirebaseAdmin` or Web Push library | Apache-2.0 | PWA push in phase 2 |
| Logging | Serilog (`Serilog.AspNetCore`, sinks: Console, Seq) | Apache-2.0 | Enrich with `TenantId`, `UserId`, `CorrelationId` |
| Tracing/metrics | OpenTelemetry .NET (ASP.NET Core, HttpClient, EF Core, Npgsql) | Apache-2.0 | Export OTLP to Grafana/Seq |
| Health checks | `AspNetCore.HealthChecks.NpgSql`, `.Redis` | Apache-2.0 | `/health/live`, `/health/ready` |
| Feature flags | `Microsoft.FeatureManagement.AspNetCore` + custom tenant filter | MIT | Per-tenant / per-plan rollout |
| Secrets | Azure Key Vault or AWS Secrets Manager config provider | MIT / Apache-2.0 | Never in appsettings |
| Static analysis | .NET analyzers, `SonarAnalyzer.CSharp`, `Meziantou.Analyzer` | LGPL / MIT | Warnings as errors in CI |
| Local dev | .NET Aspire (AppHost) or docker-compose | MIT | Postgres, Redis, Seq, MinIO with one command |

**Testing packages**

| Purpose | Package | Licence |
| --- | --- | --- |
| Test framework | xUnit v3 | Apache-2.0 |
| Assertions | Shouldly (or AwesomeAssertions) | BSD / Apache-2.0 |
| Mocks | NSubstitute | BSD-3 |
| Real Postgres/Redis in tests | Testcontainers for .NET | MIT |
| Reset DB between tests | Respawn | Apache-2.0 |
| API tests | `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory) | MIT |
| Architecture rules | NetArchTest.Rules or ArchUnitNET | MIT / Apache-2.0 |
| Fake data | Bogus | MIT |
| Fake HTTP providers | WireMock.Net | Apache-2.0 |
| Snapshot (OpenAPI contract) | Verify | MIT |
| Load testing | k6 | AGPL (tool only, not shipped) |

FluentAssertions v8+ also moved to a commercial licence, which is why Shouldly/AwesomeAssertions are listed instead.

## SOLID in this codebase

Each principle maps to a concrete, checkable habit in CoachDB, not a slogan.

| Principle | Rule in CoachDB | Where you see it |
| --- | --- | --- |
| **S** Single responsibility | One handler = one use case; one endpoint file = one route; entities own only their own rules | `CancelSubscriptionHandler`, `Subscription.Cancel()` |
| **O** Open/closed | New behaviour = new class registered in DI, not an edited `switch` | New payment provider, question type, notification channel |
| **L** Liskov substitution | Every implementation honours the interface contract fully (no `NotSupportedException`) | All `IPaymentGateway` implementations pass the same contract test suite |
| **I** Interface segregation | Small, role-based interfaces | `IPaymentCheckout` vs `IPaymentRefunds` vs `IWebhookVerifier` |
| **D** Dependency inversion | Application defines interfaces; Infrastructure implements them; DI wires them | `IFileStorage` in Application, `S3FileStorage` in Infrastructure |

**S — one reason to change**

```csharp
// Domain: the rule lives with the data it protects
public sealed class Subscription : AggregateRoot<SubscriptionId>
{
    public Result Cancel(string reason, IClock clock)
    {
        if (Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired)
            return SubscriptionErrors.NotActive;          // stable error code
        Status = SubscriptionStatus.Cancelled;
        CancelledAt = clock.UtcNow;
        Raise(new SubscriptionCancelled(Id, TenantId, reason));
        return Result.Success();
    }
}

// Application: orchestration only, no rules, no HTTP
internal sealed class CancelSubscriptionHandler(
    ISubscriptionRepository repo, IClock clock) : ICommandHandler<CancelSubscriptionCommand, Result>
{
    public async ValueTask<Result> Handle(CancelSubscriptionCommand cmd, CancellationToken ct)
    {
        var sub = await repo.GetAsync(cmd.SubscriptionId, ct);
        if (sub is null) return SubscriptionErrors.NotFound;
        return sub.Cancel(cmd.Reason, clock);             // unit of work commits in a behaviour
    }
}
```

**O — add, don't edit**

```csharp
public interface IQuestionType
{
    string Key { get; }                                  // "single_choice", "body_map"…
    Result Validate(QuestionDefinition q, JsonElement answer);
}

// Adding "body_map" later = this new class + one DI line. Nothing else changes.
internal sealed class BodyMapQuestionType : IQuestionType { … }

services.AddSingleton<IQuestionType, BodyMapQuestionType>();
```

**L — substitutable providers.** A shared abstract test class (`PaymentGatewayContractTests`) runs the same scenarios (create checkout, verify a valid webhook, reject a forged one, handle a duplicate) against Paymob, Fawry and Stripe adapters. An adapter that cannot pass is not merged.

**I — small interfaces**

```csharp
public interface IPaymentCheckout { Task<CheckoutSession> CreateAsync(CheckoutRequest r, CancellationToken ct); }
public interface IPaymentRefunds  { Task<Result> RefundAsync(PaymentId id, Money amount, CancellationToken ct); }
public interface IWebhookVerifier { bool IsAuthentic(HttpRequest request, string body); }
```

A provider that can't refund via API (some cash/voucher methods) simply doesn't implement `IPaymentRefunds`, instead of throwing.

**D — depend on abstractions.** Application code asks for `IClock`, `ITenantContext`, `IFileStorage`, `IPaymentCheckout`, `INotificationChannel`, never `DateTime.UtcNow`, `HttpContext`, `AmazonS3Client`. This is what makes the domain and handlers unit-testable without a server or database.

## Design patterns

Use a pattern only where it removes a real problem in CoachDB; the table says which problem each one solves.

| Pattern | Problem it solves here | Where |
| --- | --- | --- |
| **CQRS** (command/query separation) | Writes need rules and transactions; reads need speed and projections | Every module: `Commands/` load aggregates, `Queries/` project straight to DTOs (`AsNoTracking`, Dapper for dashboard) |
| **Mediator** | Endpoints shouldn't know handlers; cross-cutting code needs one place to hook in | Endpoint → `ISender.Send(command)` |
| **Pipeline / Decorator** | Validation, logging, transactions, audit repeated in every handler | `ValidationBehavior`, `LoggingBehavior`, `UnitOfWorkBehavior`, `AuditBehavior` |
| **Aggregate root** (DDD) | Invariants that span several rows | `Subscription`, `NutritionPlan` (meals + items), `WorkoutProgram`, `Questionnaire`, `CheckIn` |
| **Value object** | Primitive obsession and invalid values | `Money(Amount, Currency)`, `Macros`, `Email`, `TenantId`, `Slug` |
| **Strongly typed IDs** | Passing a `ClientId` where a `CoachId` is expected | `readonly record struct ClientId(Guid Value)` |
| **Domain events** | Side effects buried inside entities | `CheckInSubmitted`, `SubscriptionActivated` |
| **Transactional outbox** | An event lost if the process dies after commit | Integration events saved in the same transaction, dispatched by a job |
| **Idempotent consumer / inbox** | Gateways send the same webhook twice | `processed_messages` / `payment_events` unique on provider event id |
| **Repository + Unit of Work** | Aggregate loading and saving scattered across handlers | One repository per aggregate root; `DbContext` is the unit of work. No generic `IRepository<T>` |
| **Specification** (light) | The same query rules copied in several places | `ActiveSubscriptionsFor(clientId)`, `DueCheckIns(date)` |
| **Strategy** | Different behaviour per provider/type selected at runtime | `IPaymentGateway`, `IQuestionType`, `INotificationChannel` |
| **Factory / resolver** | Picking the right strategy by key | `PaymentGatewayResolver.Get("paymob")` using keyed DI services |
| **Adapter / Anti-corruption layer** | Provider payloads leaking into the domain | `PaymobAdapter` maps Paymob JSON ↔ `PaymentResult` |
| **State machine** | Illegal status jumps (expired → active) | `Subscription`: pending → active ⇄ paused → cancelled/expired; `CheckIn`: draft → submitted → reviewed |
| **Result pattern** | Exceptions used for expected failures | `Result` / `Result<T>` with `Error(Code, Message, Type)` mapped to problem details |
| **Options pattern** | Config read ad hoc, unvalidated | `PaymobOptions` with `ValidateDataAnnotations().ValidateOnStart()` |
| **Snapshot** | Editing a package changes past purchases | Price, features, duration copied into `Subscription` at purchase |
| **Soft delete / archive** | Hard delete breaks history | `IArchivable` + global query filter |
| **Circuit breaker + retry** | A slow gateway takes the API down | Polly via `AddStandardResilienceHandler()` on provider clients |

**Pipeline order for every command**

1. `LoggingBehavior` — logs the command name, tenant, duration.
2. `ValidationBehavior` — FluentValidation; returns `Result` with `validation` errors (no exception).
3. `AuthorizationBehavior` — checks the permission declared on the command (`[RequiresPermission("plans.write")]`).
4. `UnitOfWorkBehavior` — wraps the handler in a transaction, calls `SaveChanges`, writes outbox messages.
5. `AuditBehavior` — records before/after for commands marked `IAuditable` in `audit_logs`.

Queries skip steps 4–5.

**Strategy + keyed DI for payment providers**

```csharp
services.AddKeyedScoped<IPaymentGateway, PaymobGateway>("paymob");
services.AddKeyedScoped<IPaymentGateway, FawryGateway>("fawry");
services.AddKeyedScoped<IPaymentGateway, StripeGateway>("stripe");

// Webhook endpoint stays generic: POST /webhooks/{provider}
app.MapPost("/webhooks/{provider}", async (string provider, HttpRequest req,
    IServiceProvider sp, ISender sender, CancellationToken ct) =>
{
    var gateway = sp.GetKeyedService<IPaymentGateway>(provider);
    if (gateway is null) return Results.NotFound();
    var body = await new StreamReader(req.Body).ReadToEndAsync(ct);
    if (!gateway.Verifier.IsAuthentic(req, body)) return Results.Unauthorized();
    await sender.Send(new StoreWebhookCommand(provider, body), ct);  // store once, process in a job
    return Results.Ok();
});
```

**Patterns to avoid here:** generic repositories over EF Core, a shared "Common" project with business logic, AutoMapper-style magic mapping of entities into API responses, and exceptions for business rule failures.

## Cross-cutting concerns

Everything every module needs is built once in `BuildingBlocks` during Phase 0, before any feature, so modules only write business code.

**Request pipeline (middleware order in `Program.cs`)**

1. Exception handler → RFC 7807 problem details (`IExceptionHandler`), never a stack trace to the client.
2. Correlation id + Serilog request logging.
3. HTTPS redirection, CORS (Vue origins only), security headers.
4. Rate limiter.
5. Authentication (JWT).
6. **Tenant resolution** middleware: subdomain → custom domain → `X-Tenant` header; checks membership; sets `ITenantContext`; 403 if not a member.
7. Authorization (permission policies).
8. Endpoints.

**Multi-tenancy, two independent layers**

```csharp
// 1. EF Core: every tenant-owned entity implements ITenantOwned
modelBuilder.Entity<Client>().HasQueryFilter(c => c.TenantId == _tenant.TenantId);
// SaveChanges interceptor stamps TenantId on new entities and rejects a mismatch

// 2. Postgres RLS: interceptor sets the session variable per transaction
public sealed class TenantConnectionInterceptor(ITenantContext tenant) : DbTransactionInterceptor
{
    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection conn, TransactionEndEventData e, DbTransaction tx, CancellationToken ct = default)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT set_config('app.tenant_id', @id, true)";
        cmd.Parameters.Add(new NpgsqlParameter("id", tenant.TenantId.ToString()));
        await cmd.ExecuteNonQueryAsync(ct);
        return tx;
    }
}
```

Rule: every request that touches the DB runs inside a transaction (the `UnitOfWorkBehavior` for commands, a read-only transaction for queries), so RLS is always active. The API connects as `app_user`; jobs that must cross tenants use a separate `app_system` connection string and are reviewed individually.

**Authentication and authorization**

- ASP.NET Core Identity on `users`; JWT access token 15 min; refresh token rotation with reuse detection (a reused refresh token revokes the whole family).
- Permissions as constants (`Permissions.Plans.Write = "plans.write"`); role → permission map stored as data; one policy per permission registered automatically.
- Resource checks in handlers: a coach without `clients.read_all` only gets clients assigned to him (a query specification, not an `if` in every endpoint).

**Errors and responses**

| Error type | HTTP | Example code |
| --- | --- | --- |
| Validation | 400 | `validation_failed` (with field errors) |
| Unauthorized | 401 | `auth_required` |
| Forbidden / feature gate | 403 | `subscription_required`, `plan_limit_reached` |
| Not found (incl. other tenant's id) | 404 | `client_not_found` |
| Conflict | 409 | `subscription_not_active`, `concurrency_conflict` |
| Unexpected | 500 | `internal_error` (details only in logs, with correlation id) |

**Concurrency.** Optimistic concurrency on aggregates using Postgres `xmin` (`UseXminAsConcurrencyToken`); a conflict returns 409 and the Vue app reloads.

**Idempotency.** `POST` endpoints that create money or subscriptions accept an `Idempotency-Key` header; the key and response are stored for 24 h. Webhooks are deduplicated by the provider's event id with a unique index.

**Outbox and events**

1. Handler changes aggregates; aggregates raise domain events.
2. `UnitOfWorkBehavior` dispatches domain events in-process, converts integration events into `outbox_messages` rows, and commits everything in one transaction.
3. `OutboxDispatcher` (Hangfire, every few seconds) reads unprocessed rows with `FOR UPDATE SKIP LOCKED`, publishes to in-process subscribers, marks them processed; failures retry with backoff and land in a dead-letter state after N attempts with an alert.
4. Each subscriber records the message id in its own `inbox` table so a re-delivery has no effect.

**Background jobs (Hangfire)**

- Jobs are thin: they call a command through the mediator, so the same validation, transaction and audit apply.
- Per-tenant jobs loop over tenants and set `ITenantContext` per iteration (so RLS still applies).
- All jobs are idempotent and safe to run twice; the Hangfire dashboard sits behind the platform-admin role.

**Caching.** `HybridCache` for package lists, food search and permission maps. Keys always include the tenant: `t:{tenantId}:packages:public`. Invalidate on the domain event (`PackageUpdated`), never by timeout alone.

**Logging and tracing.** Structured Serilog with `TenantId`, `UserId`, `CorrelationId` on every line; OpenTelemetry traces across HTTP → handler → EF → outbound HTTP; never log tokens, passwords, card data or medical notes.

**Time, money, localisation.** `IClock` (or `TimeProvider`) everywhere, UTC in the DB; `Money` value object with `decimal` + ISO currency; the API returns error codes, the Vue app translates them.

## Data access and database evolution

The existing tested 36-table schema becomes migration #1 (the baseline); from then on, every change is an EF Core migration owned by the module whose tables it touches.

**Mapping the existing schema to modules**

1. Group the 36 tables by owning module (e.g. `subscriptions`, `packages` → Packages; `payments`, `payment_events`, `payouts` → Payments). A table has exactly one owner.
2. Shared reference data (`users`, `tenants`, `tenant_members`) belongs to Identity/Tenants; others read it only through their `Contracts`.
3. Cross-module foreign keys are allowed in the database (integrity matters), but code never joins across modules.
4. Write EF configurations (`IEntityTypeConfiguration<T>`) by hand to match the schema exactly; use `dotnet ef dbcontext scaffold` only as a reference.

**Baseline and migrations**

- Migration `0001_Baseline` executes the tested schema SQL (tables, RLS policies, triggers, constraints) via `migrationBuilder.Sql(...)` so a fresh environment is identical to what you tested.
- One migrations history table per module schema (`MigrationsHistoryTable("__ef_migrations", "payments")`).
- Migrations run as a separate step in the pipeline (an EF migration bundle executed with the `app_migrator` role), never at app startup.
- **Expand → migrate → contract** for every breaking change: add the new column, deploy code that writes both, backfill, switch reads, then drop the old column in a later release. Zero-downtime deploys follow.
- RLS policies, triggers and DB-level rules (photos only after payment, training only on training packages) live in migrations as SQL and are covered by integration tests.

**Database roles**

| Role | Used by | Can bypass RLS |
| --- | --- | --- |
| `app_migrator` | CI/CD migration step | yes (owner) |
| `app_user` | API requests | no |
| `app_system` | Cross-tenant jobs (webhooks, renewals, platform billing) | yes, read/write limited to needed tables |
| `app_readonly` | Reports on the read replica | no |

**Query rules**

- Commands load aggregates through repositories; queries project with `Select` to DTOs and use `AsNoTracking()`.
- No `Include` chains deeper than two levels; no lazy loading (disabled).
- Every list endpoint uses keyset (cursor) paging, a max `limit` of 100, and an index matching its sort order.
- Turn on `EnableSensitiveDataLogging` only locally; log slow queries (> 200 ms) with their tenant.
- Use compiled queries for the hottest paths (dashboard, public package list) once measured.
- Plan partitioning of `messages`, `nutrition_logs`, `workout_set_logs` by month (Phase 3) from day one: include the timestamp in their primary key now so partitioning later needs no re-keying.

## Testing strategy and quality gates

A pull request merges only when all seven gates below pass; the architecture and isolation tests are what keep the design honest after month six.

| Layer | What it proves | Tools | Runs |
| --- | --- | --- | --- |
| Unit | Domain rules: macro maths, subscription state machine, permission maps, value objects | xUnit, Shouldly, NSubstitute | Every push, < 30 s |
| Integration (per module) | Handlers + EF + real Postgres: RLS, triggers, constraints, migrations | Testcontainers, Respawn | Every PR |
| API | Endpoints, auth, status codes, problem-detail codes | WebApplicationFactory | Every PR |
| Tenant isolation | Tenant B can't read or write tenant A's ids on any endpoint | Generated from the endpoint list | Every PR, blocks merge |
| Architecture | Layer and module boundaries from the dependency table | NetArchTest / ArchUnitNET | Every PR |
| Contract | No breaking change in `/api/v1` OpenAPI | Verify snapshot + diff | Every PR |
| Provider contract | Every `IPaymentGateway` passes the same scenarios incl. duplicate and out-of-order webhooks | WireMock.Net, provider sandboxes | Every PR (mocked), nightly (sandbox) |
| Load | p95 < 300 ms reads / < 500 ms writes at 1,000 coaches, 50,000 clients | k6 on staging with seeded data | Before each phase release |

**Example architecture test**

```csharp
[Fact]
public void Domain_must_not_depend_on_infrastructure_or_other_modules()
{
    var result = Types.InAssembly(typeof(Subscription).Assembly)
        .ShouldNot()
        .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
                             "CoachDB.Payments", "CoachDB.Nutrition")
        .GetResult();
    result.IsSuccessful.ShouldBeTrue();
}
```

**Tenant isolation suite (how it scales to every endpoint)**

1. Seed tenant A and tenant B with one of each resource.
2. Discover all endpoints from `EndpointDataSource`; each route with an `{id}` gets a test case automatically.
3. Call each as a user of tenant B with tenant A's ids: expect 404 (or an empty list).
4. Directly in SQL as `app_user` with `app.tenant_id = B`, try to insert/update A's rows: expect an RLS error.
5. A new endpoint without isolation coverage fails the build (the test checks the list is complete).

**Quality gates in CI**

- [ ] Build with warnings as errors and analyzers on
- [ ] Unit + integration + API tests green
- [ ] Tenant isolation suite green
- [ ] Architecture tests green
- [ ] OpenAPI contract unchanged or additive only
- [ ] Coverage ≥ 80% on Domain and Application projects (not a target for Infrastructure)
- [ ] One reviewer approval; `CODEOWNERS` for Payments and Identity require a second

## DevOps, environments and observability

Every merge to `main` is built once into a Docker image, tested, deployed to staging automatically, and promoted to production with one approval: the same image, never rebuilt.

**Environments**

| Environment | Purpose | Data | Deploys |
| --- | --- | --- | --- |
| Local | Development | Seeded fake data | `dotnet run` on the Aspire AppHost (Postgres, Redis, Seq, MinIO) |
| CI | Tests | Testcontainers, thrown away | Every PR |
| Staging | Demo, QA, load tests, provider sandboxes | Seeded + anonymised | Automatic on merge to `main` |
| Production | Coaches and clients | Real | Manual approval of a staging-tested image |

**Pipeline (GitHub Actions)**

1. Restore, build (warnings as errors), format check (`dotnet format --verify-no-changes`).
2. Unit, architecture and contract tests.
3. Integration, API and tenant isolation tests (Testcontainers).
4. Dependency and secret scan (`dotnet list package --vulnerable`, GitHub Dependabot, CodeQL).
5. Build the Docker image (chiseled .NET runtime base, non-root user), tag with the commit SHA, push to the registry.
6. Build an EF migration bundle as a second artifact.
7. Staging: run the migration bundle, deploy the image, run smoke tests against `/health/ready` and key endpoints.
8. Production (approval): migrations, then a rolling deploy; automatic rollback if health checks fail.

**Hosting (works on either cloud — the open question in the PRD)**

| Need | Azure | AWS |
| --- | --- | --- |
| API containers | Azure Container Apps | ECS Fargate |
| PostgreSQL 16+ | Azure Database for PostgreSQL Flexible Server | Amazon RDS for PostgreSQL |
| Redis | Azure Cache for Redis | ElastiCache |
| Files | Blob Storage (private) | S3 (private) |
| Secrets | Key Vault | Secrets Manager |
| Infra as code | Bicep or Terraform | Terraform or CDK |

Start with 2 API instances behind the load balancer from day one, so statelessness is proven early.

**Observability**

- Logs: Serilog → Seq (or Grafana Loki); every line has tenant, user and correlation id.
- Traces and metrics: OpenTelemetry → Grafana Tempo/Prometheus or the cloud's APM.
- Dashboards: request rate, error rate, p95 latency per endpoint, DB pool usage, outbox lag, Hangfire failures, webhook failures per provider.
- Alerts: 5xx rate > 1% for 5 min, p95 > 500 ms, outbox lag > 1 min, any failed webhook signature spike, backup failure.
- Backups: point-in-time recovery kept 30 days; a restore drill into a scratch environment every month.

**Security baseline:** TLS everywhere, HSTS, private DB and storage networks, least-privilege DB roles, secrets only in the vault, dependency updates weekly, OWASP ASVS level 2 as the checklist for the auth and payment modules.

## Coding conventions and team rules

Write these rules into `CONTRIBUTING.md` and enforce as many as possible with analyzers, `.editorconfig` and architecture tests, so code review focuses on logic.

**Code**

- Classes are `sealed` and `internal` by default; only `Contracts` and endpoint registration are `public`.
- Records for commands, queries, DTOs, events; primary constructors for handlers.
- Nullable reference types on; no `!` (null-forgiving) without a comment.
- Every async method takes a `CancellationToken` and passes it on.
- No `DateTime.Now`, `Guid.NewGuid()` in domain code: use `IClock` / `TimeProvider` and ID factories (use UUID v7 `Guid.CreateVersion7()` for sortable keys).
- Expected failures return `Result`; exceptions only for bugs and infrastructure failures.
- Entities have private setters; state changes only through methods that enforce rules.
- No business logic in endpoints, EF configurations, or jobs.
- API responses are DTOs, never entities.

**Naming**

| Thing | Pattern | Example |
| --- | --- | --- |
| Command | `<Verb><Noun>Command` | `SubmitCheckInCommand` |
| Query | `Get<Noun>Query` / `List<Nouns>Query` | `ListClientsQuery` |
| Handler | `<Command/Query name minus suffix>Handler` | `SubmitCheckInHandler` |
| Domain event | past tense | `CheckInSubmitted` |
| Error code | `snake_case`, module-scoped class | `CheckInErrors.AlreadyReviewed` → `checkin_already_reviewed` |
| Endpoint file | `<Verb><Noun>Endpoint` | `SubmitCheckInEndpoint` |
| DB | `snake_case`, plural tables | `check_ins.submitted_at` |

**Git and review**

- Trunk-based: short-lived branches, PRs under ~400 changed lines, squash merge.
- Conventional commits (`feat(payments): add fawry refunds`) to generate changelogs.
- Every PR that adds an endpoint adds its validator, its tests, and (automatically) its isolation case.
- Record significant decisions as ADRs in `docs/adr/` (e.g. ADR-001 modular monolith, ADR-002 Mediator over MediatR, ADR-003 own outbox).

**Definition of done for a feature**

- [ ] Command/query, handler, validator, endpoint in one slice folder
- [ ] Domain rules unit-tested; handler integration-tested against Postgres
- [ ] Permission declared; isolation suite passes
- [ ] Error codes documented in OpenAPI; Vue client regenerated
- [ ] Logs and audit entries checked; no sensitive data logged
- [ ] Migration is additive (expand/contract) if the schema changed

## Build roadmap

Spend Phase 0 building the skeleton and every cross-cutting piece, then deliver Phase 1 modules in dependency order, each one end-to-end (API + tests + Vue screen) before the next starts.

**Phase 0: foundation (3 weeks, 2 developers)**

1. **Week 1: skeleton**
   - Solution layout, `Directory.Build.props`, central package management, `.editorconfig`, analyzers.
   - Aspire AppHost or docker-compose: Postgres, Redis, Seq, MinIO.
   - SharedKernel: `Entity`, `AggregateRoot`, `ValueObject`, strongly typed IDs, `Result`/`Error`.
   - Application.Abstractions: command/query interfaces, Mediator, pipeline behaviours.
   - GitHub Actions: build + unit tests; architecture tests with the dependency rules.
2. **Week 2: data and tenancy**
   - Baseline migration from the tested schema; DB roles; per-module `DbContext` base class.
   - Tenant resolution middleware, `ITenantContext`, query filters, RLS interceptor.
   - Tenant isolation test harness (works with zero endpoints, grows automatically).
   - Outbox table, dispatcher job, inbox table; Hangfire on Postgres.
3. **Week 3: platform services**
   - Identity: register, login, refresh rotation, `/me`; permission policies.
   - Problem-details error handling, correlation id, Serilog, OpenTelemetry, health checks.
   - OpenAPI + Scalar; Vue typed client generation wired into the frontend build.
   - Staging environment, Docker image, migration bundle, deploy pipeline.

Exit check: a user can sign up and log in on staging, every log line carries tenant and correlation id, and CI fails if a test endpoint leaks across tenants.

**Phase 1: modules in dependency order (10–12 weeks)**

| # | Module | Depends on | Notes |
| --- | --- | --- | --- |
| 1 | Tenants (create business, trial, branding) | Identity | Needed by everything else |
| 2 | Team (invites, roles) | Tenants | Proves the permission model |
| 3 | Clients | Tenants, Team | First real isolation-heavy module |
| 4 | Packages & subscriptions | Clients | State machine + snapshot pattern |
| 5 | Payments (Paymob) | Packages | `IPaymentGateway`, webhooks, idempotency, outbox → `PaymentSucceeded` |
| 6 | Questionnaires | Clients | Question-type registry (Strategy) |
| 7 | Media | Tenants | Signed URLs, image processing job |
| 8 | Progress (check-ins, photos, review) | Media, Packages | DB rule: photos only after payment |
| 9 | Nutrition (foods, plans, logs, adherence) | Clients, Packages | Templates + copy-to-client |
| 10 | Chat & email notifications | Clients | SignalR hub, unread counts, outbox subscribers |
| 11 | Coach dashboard | All above | Read-only queries, cached |

Run a load test and the full Phase 1 acceptance checklist from the PRD before calling Phase 1 done. Phase 2 (Training, Fawry, Stripe, push, reminders, SaaS billing) then plugs in through the same interfaces with no change to the Phase 1 modules.

**First five things to do this week**

- [ ] Install the .NET 10 SDK and create the solution skeleton above
- [ ] Write ADR-001 (modular monolith) and ADR-002 (library choices and licences)
- [ ] Map the 36 tables to their owning modules
- [ ] Decide Azure vs AWS (affects only Infrastructure adapters and IaC)
- [ ] Set up the CI pipeline with architecture tests before writing any feature

**Sources**

- [.NET 8 and .NET 9 end of support (.NET Blog)](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)
- [AutoMapper and MediatR commercial editions (Jimmy Bogard)](https://www.jimmybogard.com/automapper-and-mediatr-commercial-editions-launch-today/)
- [CoachDB Backend PRD](https://claude.ai/artifact/Nu1WFzMSRVAz6aVtFqhUT7)
