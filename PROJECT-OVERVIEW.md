# CoachDB API — Project Overview and Status

As of 2026-10-26. This explains how the code is organised and how far the build has progressed against the
[Architecture & implementation plan](CoachDB-Backend-Architecture-Plan.md) and the [Backend PRD](CoachDB-Backend-PRD.md).
For setup commands and the API contract, see the [README](README.md); for coding rules, see [CONTRIBUTING](CONTRIBUTING.md).
Detailed write-ups of each finished module are in [progress/](progress/), starting with [Tenants in Phase 1](progress/tenant_in_phase_1.md).

## In one paragraph

CoachDB is a multi-tenant SaaS backend for fitness coaches (packages, payments, nutrition, training, check-ins,
chat). It is a **.NET 10 modular monolith** on **PostgreSQL 16**: one deployable API that will hold 12 business
modules, each with its own database schema and Clean Architecture layers. Today the **foundation (Phase 0)** is
mostly built, **Identity** (register, login, refresh, logout, `/me`) works end to end, and so does **Tenants**
(create a business with a trial, settings and branding, SaaS plans, payout accounts, the platform admin API).
Every tenant-scoped request now goes through tenant resolution and Postgres RLS. The other 10 modules don't exist yet.

## The three levels of structure

| Level | What it separates | Where you see it |
|---|---|---|
| **Modular monolith** | Business areas (Identity, Payments, Nutrition…) | `src/Modules/<Module>/`, one Postgres schema per module |
| **Clean Architecture** | Business rules from frameworks | Each module splits into Domain → Application → Infrastructure → Api |
| **Vertical slices** | One feature from another | `Auth/Login/`, `Auth/Register/`: command, handler and validator together |

Modules may talk to each other only through another module's `*.Contracts` project (interfaces and events),
never through its tables or internals. Architecture tests enforce this.

## Folder map

``` 
CoachDB.slnx                      solution (new XML .slnx format)
global.json                       pins SDK 10.0.x
Directory.Build.props             net10.0, nullable, warnings-as-errors, analyzers for every project
Directory.Packages.props          every NuGet version in one place (central package management)
dotnet-tools.json                 local tool: dotnet-ef

src/
  Host/
    CoachDB.Api/                  the web app: Program.cs (middleware order), security, health, OpenAPI
    CoachDB.Migrator/             console app that applies all module migrations, then grants DB roles
  BuildingBlocks/                 shared code every module builds on
    CoachDB.SharedKernel/         Entity, AggregateRoot, DomainEvent, Money, Result, Error
    CoachDB.Application.Abstractions/  command/query interfaces, pipeline behaviors, ITenantContext,
                                  permissions, cursor paging
    CoachDB.Infrastructure.Common/     ModuleDbContext base, tenant filters + RLS interceptor,
                                  outbox/inbox, unit of work, JWT options
    CoachDB.Api.Common/           IEndpoint, Result → HTTP mapping, problem details, correlation id, JSON
  Modules/
    Identity/
      CoachDB.Identity.Domain/          User, Email, UserId, RefreshToken, IdentityErrors
      CoachDB.Identity.Application/     Register / Login / Refresh / Logout commands, GetCurrentUser query
      CoachDB.Identity.Infrastructure/  IdentityDbContext, EF configs, repositories, password hasher,
                                        JWT issuer, migrations
      CoachDB.Identity.Api/             Minimal API endpoints + refresh-token cookie
      CoachDB.Identity.Contracts/       IUserDirectory, UserRegistered event (the module's public face)
    Tenants/
      CoachDB.Tenants.Domain/           Tenant, Slug, TenantMember, PlatformPlan, TenantPlanSubscription,
                                        TenantPaymentAccount, TenantsErrors
      CoachDB.Tenants.Application/      create/update business, my businesses, plans, payout accounts, admin;
                                        role → permission map
      CoachDB.Tenants.Infrastructure/   TenantsDbContext, configs, repositories, ITenantResolver, migrations (+ RLS SQL)
      CoachDB.Tenants.Api/              Minimal API endpoints (tenant, plans, payment accounts, /admin)
      CoachDB.Tenants.Contracts/        ITenantDirectory, ITenantPlanLimits, TenantCreated/Suspended/Reactivated

tests/
  CoachDB.BuildingBlocks.UnitTests/  Result, Money, paging, pipeline behaviors
  CoachDB.ArchitectureTests/         layer dependency rules (NetArchTest)
  CoachDB.Api.Tests/                 HTTP pipeline + error mapping (WebApplicationFactory, no DB)
  CoachDB.IntegrationTests/          full auth flow + persistence on real Postgres (Testcontainers)
  Modules/CoachDB.Identity.UnitTests/  Identity domain, handlers, validators, tokens
  Modules/CoachDB.Tenants.UnitTests/   Tenants domain rules, validators, role permissions, admin cursor

deploy/
  docker-compose.yml              Postgres 16 (port 5433), Redis, Seq, MinIO (+ bucket init)
  postgres/init/01-roles.sql      creates app_migrator, app_user, app_system, app_readonly
  Dockerfile                      API container image
db/reference/schema_import.sql    original 36-table DDL, kept as reference (the model is now code-first)
progress/                         one detailed write-up per finished module (tenant_in_phase_1.md, ...)
.github/workflows/ci.yml          format, build, migration drift check, tests, vulnerable packages, image
```

### Dependency direction inside a module

```
Api ──► Application ──► Domain ◄── Infrastructure
                 ▲                      │
                 └──────────────────────┘   (Infrastructure implements Application's interfaces)
Other modules ──► Contracts only
```

## How a request flows

```
HTTP ─► middleware: exception handler → status-code pages → correlation id → Serilog request log
        → HTTPS redirect → CORS → rate limiter → authentication → tenant resolution → authorization
     ─► endpoint (IEndpoint, under /api/v1) builds a Command/Query
     ─► Mediator pipeline: Logging → Validation (FluentValidation)
        → Authorization ([RequiresPermission] / [RequiresTenant] / [RequiresPlatformAdmin])
        → UnitOfWork (commands only: one transaction, SaveChanges)
     ─► handler returns Result<T>
     ─► endpoint maps success → 200/201/204, failure → RFC 7807 problem details with a stable `code`
```

When an aggregate raises a domain event (for example `UserRegistered`), `ModuleDbContext` writes it to the
module's `outbox_messages` table **in the same transaction**. A background `OutboxDispatcher` picks up rows
every 2 s (`FOR UPDATE SKIP LOCKED`), runs the handlers, and records them in `inbox_messages` so a message
re-delivered later is not handled twice.

## Data and multi-tenancy

- **One schema per module.** Identity owns `identity.users`, `identity.refresh_tokens`; Tenants owns
  `tenants.tenants`, `platform_plans`, `tenant_members`, `tenant_plan_subscriptions`, `tenant_payment_accounts`.
  Each schema also has its own `outbox_messages`, `inbox_messages` and `__ef_migrations`.
- **Four DB roles.** `app_migrator` owns schemas and runs migrations; `app_user` serves API requests (RLS applies);
  `app_system` is for cross-tenant jobs (bypasses RLS); `app_readonly` is for reports.
- **Tenant resolution.** `TenantResolutionMiddleware` (after authentication) reads the slug from the subdomain of
  `Tenancy:RootDomain` or the `X-Tenant` header, asks the Tenants module's `ITenantResolver`, and then sets
  `ITenantContext` and adds the member's permissions as `perm` claims. Unknown slug → `404 tenant_not_found`,
  suspended → `403 tenant_suspended`, not a member → `403 tenant_access_denied`. A request without a tenant passes
  through; tenant-scoped use cases then answer `400 tenant_required`. Endpoints marked `.WithoutTenant()`
  (sign-in, creating a business, `/me/tenants`, `/public/*`, `/plans`, `/admin/*`) ignore the header.
- **Two isolation layers, both active:**
  1. EF global query filter `TenantId == current tenant` on every `ITenantOwned` entity, plus a soft-delete filter.
  2. Postgres RLS. `TenantRlsConnectionInterceptor` sets `app.tenant_id` and `app.user_id` on every connection;
     `RlsSql` writes the `tenant_isolation` policies in migrations. Today they cover `tenant_members`,
     `tenant_plan_subscriptions` and `tenant_payment_accounts`. `tenant_members` also lets a user *read* their own
     rows in every tenant (for "my businesses" and resolution); writes always need the current tenant.
  `TenantStampingInterceptor` fills `TenantId` on new rows and throws on a cross-tenant write.
- **Global tables.** `identity.*`, `tenants.tenants` and `tenants.platform_plans` have no RLS: a user belongs to
  several businesses, and a business is found by slug before any tenant is chosen. The application layer guards them.
- The API **never migrates on startup**. Run `dotnet run --project src/Host/CoachDB.Migrator`.

## Identity module: what works

| Endpoint | Behaviour |
|---|---|
| `POST /api/v1/auth/register` | Creates a user (email, password, full name, locale `ar`/`en`), returns access token and sets the refresh cookie → 201. `409 email_taken` if the email is in use |
| `POST /api/v1/auth/login` | Verifies the password, returns access token and refresh cookie. `401 invalid_credentials` for both unknown email and wrong password |
| `POST /api/v1/auth/refresh` | Rotates the refresh token (cookie or JSON body). Re-using an already rotated token revokes the whole session: `401 refresh_token_reused` |
| `POST /api/v1/auth/logout` | Revokes the session and clears the cookie; safe to call twice → 204 |
| `GET /api/v1/me` | The signed-in user's profile; `401 auth_required` without a token |

Security details: passwords hashed with ASP.NET Core Identity's `PasswordHasher` (re-hashed automatically when
settings change). Access tokens are JWTs that last 15 min. Refresh tokens last 30 days, only their SHA-256 hash
is stored, and they are grouped in families so a stolen token can be detected. Auth endpoints have a stricter
rate limit (5/min by default, 100/min in Development) than the rest of the API (300/min).

## Tenants module: what works

| Endpoint | Who | Behaviour |
|---|---|---|
| `POST /api/v1/tenants` | signed in | Creates a business → 201 + `Location /api/v1/tenant`. Starts a 14-day trial of `starter`, makes the caller `owner`. `409 slug_taken`, `403 tenant_limit_reached` (3 owned max) |
| `GET /api/v1/tenants/slug-availability?slug=` | anyone | `{ slug, available, reason }`, reason = `slug_invalid` / `slug_reserved` / `slug_taken` |
| `GET /api/v1/me/tenants` | signed in | The user's businesses with `role` and `permissions`; send the `slug` as `X-Tenant` |
| `GET /api/v1/public/{slug}` | anyone | Public profile: name, branding, contact. 404 when unknown or suspended |
| `GET /api/v1/tenant` | any member | The current business |
| `PATCH /api/v1/tenant` | `tenant.manage` | Name, currency, time zone (IANA), locale; `branding` and `settings` replace the whole object. Custom domain is platform-only |
| `GET /api/v1/tenant/plan` | any member | Plan with limits and features, current subscription (`trialing`), usage (coaches) |
| `GET /api/v1/plans` | anyone | Active SaaS plans, cheapest first |
| `GET/POST /api/v1/tenant/payment-accounts`, `GET/DELETE /{id}`, `POST /{id}/default` | `tenant.billing` | Payout accounts (Paymob/Fawry/Stripe), one per provider, one default (a deferred DB constraint). Status `pending` until Payments verifies it |
| `GET/POST /api/v1/admin/plans`, `GET/PUT /{id}`, `POST /{id}/activate`, `/deactivate` | platform admin | Manage SaaS plans; features are a JSON map of flags |
| `GET /api/v1/admin/tenants?status=&search=&cursor=&limit=`, `GET /{id}`, `POST /{id}/suspend`, `/reactivate` | platform admin | All businesses, newest first, cursor-paged |

Roles map to permissions in `RolePermissions` (owner, coach, assistant, from the PRD's roles table). Three SaaS
plans are seeded by the migration (`starter` 19, `pro` 49, `studio` 129 USD/month); the prices are placeholders.
Settings: `Tenants:TrialDays`, `Tenants:TrialPlanCode`, `Tenants:MaxOwnedTenantsPerUser`, `Tenancy:RootDomain`.
Sample requests: `src/Host/CoachDB.Api/http/tenants.http`. Full details (data model, decisions, examples, error codes): [progress/tenant_in_phase_1.md](progress/tenant_in_phase_1.md).

**Design note.** `tenant_members` lives in Tenants, not Team as the plan's table said, because tenant resolution
reads it on every request and creating a business must add the owner in the same transaction (one module per
command). The Team module will add invites and custom roles and change members through a Tenants contract.

Not built yet: `POST /tenant/plan/checkout` (needs Payments), trial-end enforcement (platform billing job),
custom domains (Phase 3), client counts in plan usage (Clients module).

## Status against the plan

### Phase 0: foundation

| Item | Status | Notes |
|---|---|---|
| Solution layout, central packages, analyzers, warnings-as-errors | ✅ Done | |
| docker-compose: Postgres, Redis, Seq, MinIO | ✅ Done | Only Postgres is used by code so far |
| SharedKernel: Entity, AggregateRoot, Result/Error, Money, strongly typed IDs | ✅ Done | |
| Mediator + pipeline behaviors (logging, validation, authorization, unit of work) | ✅ Done | Uses the source-generated `Mediator` library (no MediatR licence) |
| Per-module `DbContext` base, DB roles, Migrator, first migration | ✅ Done | `InitialIdentity` and `InitialTenants` are applied in the local DB |
| Outbox + inbox + dispatcher | ✅ Done | Runs as a hosted `BackgroundService`, not Hangfire as the plan said |
| Tenant context, EF tenant filters, RLS interceptor, tenant stamping | ✅ Done | In use by the Tenants tables |
| Tenant resolution middleware (`X-Tenant` / subdomain → `ITenantContext`) | ✅ Done | Custom domains come in Phase 3 |
| Tenant isolation test harness | 🟡 Partial | Cross-tenant API tests and SQL-level RLS tests (as `app_user`) for the Tenants module. The generic "every endpoint × two tenants" suite is not built yet |
| Identity: register, login, refresh rotation, `/me` | ✅ Done | Plus logout |
| Permission model (`[RequiresPermission]` + behavior) | ✅ Done | Default role → permission map in code; Team makes it data (custom roles) |
| Problem details, correlation id, Serilog, health endpoints | ✅ Done | `/health/ready` has no checks registered yet (no DB or Redis check) |
| OpenTelemetry | ⏳ Not started | |
| Audit pipeline behavior | ⏳ Not started | README lists it in the pipeline; not in code |
| OpenAPI + Scalar | ✅ Done | `/scalar` outside Production |
| Vue typed client generation | ⏳ Not started | |
| CI: format, build, migration drift, tests, vulnerability scan, Docker image | ✅ Done | |
| Staging environment, migration bundle, deploy pipeline | ⏳ Not started | The image is built but not pushed or deployed |

### Phase 1 and later: business modules

| # | Module | Status |
|---|---|---|
| — | Identity | ✅ Core flows done. Missing from the PRD: email verification, forgot/reset password, Google sign-in |
| 1 | Tenants (business, trial, branding) | ✅ Done, see [progress/tenant_in_phase_1.md](progress/tenant_in_phase_1.md). Checkout for SaaS plans waits for Payments |
| 2 | Team (invites, roles) | ⏳ Next |
| 3 | Clients | ⏳ |
| 4 | Packages & subscriptions | ⏳ |
| 5 | Payments (Paymob) | ⏳ |
| 6 | Questionnaires | ⏳ |
| 7 | Media | ⏳ |
| 8 | Progress (check-ins, photos) | ⏳ |
| 9 | Nutrition | ⏳ |
| 10 | Chat & notifications | ⏳ |
| 11 | Coach dashboard | ⏳ |
| Phase 2 | Training, Fawry/Stripe, push, reminders, SaaS billing, admin | ⏳ |

## Tests

246 test cases across seven projects (theory rows counted separately), all passing:

| Project | Test cases | Needs |
|---|---|---|
| BuildingBlocks.UnitTests | 57 | nothing |
| Identity.UnitTests | 61 | nothing |
| Tenants.UnitTests | 64 | nothing |
| Api.Tests | 13 | nothing (in-memory host) |
| ArchitectureTests | 16 (one row per layer rule) | nothing |
| IntegrationTests | 35 | Docker (Testcontainers starts its own Postgres) |

Run all of them with `dotnet test --solution CoachDB.slnx`.

## Adding the next module (checklist)

1. Create `src/Modules/<Name>/CoachDB.<Name>.{Domain,Application,Infrastructure,Api,Contracts}` and add them to `CoachDB.slnx`.
2. Make the DbContext inherit `ModuleDbContext` with its own `Schema`. Mark tenant data with `ITenantOwned`.
3. Add `AddXxxModule(...).AddXxxEndpoints()` to `Program.cs` and the context type to `MigrationRunner.Modules`.
4. `dotnet ef migrations add Initial<Name> -p src/Modules/<Name>/CoachDB.<Name>.Infrastructure`, then add RLS policies for tenant tables.
5. Add a line for the module to the "Migrations match the model" step in `ci.yml`.
6. Add layer rules for the new module to `ArchitectureTests`.
