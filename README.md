# CoachDB API

Multi-tenant SaaS backend that lets fitness coaches sell packages, take payments, and run nutrition, training, check-ins and chat for their clients. It's a .NET 10 modular monolith on PostgreSQL 16, with tenant isolation enforced both in the API and in the database (RLS).

Specs: [Backend PRD](CoachDB-Backend-PRD.md) · [Architecture & implementation plan](CoachDB-Backend-Architecture-Plan.md)

## Prerequisites

- .NET SDK 10 (any 10.0.x, see `global.json`)
- Docker Desktop

## Run locally

```bash
dotnet tool restore                                   # dotnet-ef 10 (local tool)
docker compose -f deploy/docker-compose.yml up -d     # Postgres 16, Redis, Seq, MinIO
dotnet run --project src/Host/CoachDB.Migrator        # apply migrations + grants (as app_migrator)
dotnet run --project src/Host/CoachDB.Api             # http://localhost:5240/scalar
dotnet test --solution CoachDB.slnx                   # all tests (integration tests need Docker)
```

The API never migrates at startup: run the Migrator after pulling new migrations.

| Service | URL | Dev credentials |
|---|---|---|
| API docs (Scalar) | http://localhost:5240/scalar | — |
| Health | `/health/live`, `/health/ready` | — |
| Postgres | **localhost:5433**, db `coachdb` (5433 avoids a local PostgreSQL on 5432) | `postgres` / `postgres`; the API uses `app_user`, the Migrator `app_migrator` (see `deploy/postgres/init`) |
| Seq (logs) | http://localhost:5341 | — |
| MinIO console | http://localhost:9001 | `minioadmin` / `minioadmin` |

These credentials are for local development only.

## Solution layout

```
src/Host/CoachDB.Api                       Program.cs, middleware pipeline, OpenAPI
src/Host/CoachDB.Migrator                  applies every module's migrations in order, then grants roles
src/BuildingBlocks/CoachDB.SharedKernel    Entity, AggregateRoot, Result, Error, Money
src/BuildingBlocks/CoachDB.Application.Abstractions  ICommand/IQuery, pipeline behaviors, paging, contexts, permissions
src/BuildingBlocks/CoachDB.Api.Common      IEndpoint, Result → HTTP, problem details, JSON, versioning
src/BuildingBlocks/CoachDB.Infrastructure.Common  ModuleDbContext, tenant filters + RLS, outbox, unit of work
src/Modules/<Module>/CoachDB.<Module>.{Domain,Application,Infrastructure,Api,Contracts}
tests/                                     unit, integration, architecture, tenant isolation
deploy/                                    docker-compose, Dockerfile
db/reference/                              original importer DDL (the model is code-first)
```

## API contract

Every endpoint follows these rules.

### Request flow (one feature = one slice)

```
HTTP JSON ─► XxxRequest ─► XxxCommand / XxxQuery ─► ISender.Send
          ─► pipeline: Logging → Validation → Authorization → UnitOfWork → Audit
          ─► Handler returns Result<XxxResponse> ─► endpoint maps to HTTP
```

The request DTO, the command, the response DTO and the entity are separate types, and entities never go over the wire. Endpoints contain no logic, and handlers return `Error`s instead of throwing for expected failures.

### Success responses

| Action | Status | Body |
|---|---|---|
| GET one | 200 | DTO |
| GET list | 200 | `{ "items": [...], "nextCursor": "eyJ…", "hasMore": true }` |
| POST create | 201 + `Location` | created DTO |
| PATCH / PUT / action (`/cancel`, `/review`) | 200 (or 204) | updated DTO |
| DELETE / archive | 204 | — |
| Webhook | 200 / 202 | — |

Lists use cursor paging: `?cursor=&limit=`. `limit` defaults to 20 and can be at most 100. The cursor is opaque, so clients never build or parse it.

### Errors: always `application/problem+json` (RFC 7807)

```json
{
  "type": "https://coachdb.app/errors/validation_failed",
  "title": "One or more fields are invalid.",
  "status": 400,
  "code": "validation_failed",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "errors": { "price.amount": ["must_be_positive"], "name": ["required"] }
}
```

| Error type | HTTP | Example `code` |
|---|---|---|
| Validation | 400 | `validation_failed`, `invalid_request`, `tenant_required` |
| Unauthorized | 401 | `auth_required` |
| Forbidden / feature gate | 403 | `permission_denied`, `tenant_access_denied`, `subscription_required`, `plan_limit_reached` |
| Not found (including another tenant's id) | 404 | `client_not_found`, `tenant_not_found` |
| Conflict | 409 | `concurrency_conflict`, `subscription_not_active` |
| Rate limited | 429 | `rate_limited` |
| Unexpected | 500 | `internal_error` (details only in logs) |

`code` is stable and snake_case, and the frontend translates it. `title` is only an English fallback. `traceId` is the same value as the `X-Correlation-Id` response header and the correlation id in the logs.

### JSON & HTTP conventions

- Properties are camelCase. Enums are snake_case strings, for example `"pending_payment"`.
- Timestamps are ISO-8601 UTC. Date-only fields look like `"2026-10-26"`.
- IDs are UUID v7 strings. Money is `{ "amount": 1500.00, "currency": "EGP" }`.
- PATCH is a partial update: a field left out of the body is not changed.
- Headers:
  - `Authorization: Bearer <jwt>`
  - `X-Tenant: <slug>` chooses the business (from `GET /me/tenants`); tenant-scoped routes answer `400 tenant_required` without it
  - `Idempotency-Key`, required on payment and subscription POSTs
  - `X-Correlation-Id`, optional and echoed back
- Routes have the form `/api/v1/{plural-kebab-resource}/{id}/{action}`. Public routes live under `/api/v1/public/{slug}/…` and admin routes under `/api/v1/admin/…`.
- Changes within `/api/v1` are additive only. A breaking change goes into `/api/v2`.

### Slice template

```
Modules/Packages/CoachDB.Packages.Application/Packages/Create/
  CreatePackageCommand.cs     record : ICommand<PackageResponse>, [RequiresPermission("packages.write")]
  CreatePackageValidator.cs   FluentValidation; messages are error codes ("required")
  CreatePackageHandler.cs     domain call → repository → return response DTO
  PackageResponse.cs          response DTO
Modules/Packages/CoachDB.Packages.Api/Packages/
  CreatePackageEndpoint.cs    IEndpoint: MapPost("/packages") → 201 Created / problem details
  CreatePackageRequest.cs     request body record
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for conventions and the definition of done.
