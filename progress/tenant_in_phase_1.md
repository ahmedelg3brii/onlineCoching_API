# Tenants module — Phase 1, module 1

Status as of 2026-10-26: **done**. 246 tests pass (153 before this module), the build has no warnings,
`dotnet format --verify-no-changes` is clean, and the migration `InitialTenants` is applied to the local database.

This document records what was built for the Tenants module, why it was built that way, and what is still open.
For the overall project status see [PROJECT-OVERVIEW.md](../PROJECT-OVERVIEW.md).

---

## 1. Goal

From the PRD (Modules table, "Tenants & billing to the platform"):

> Create a coaching business, trial, SaaS plan, branding, domain, plan limits (max clients).
> `POST /tenants` · `GET/PATCH /tenant` · `GET /tenant/plan` · `POST /tenant/plan/checkout`

From the architecture plan: the Tenants module comes first in Phase 1 because every other module depends on it,
and it is where Phase 0 step 6 (tenant resolution middleware) and the first Row-Level Security policies arrive.

---

## 2. Scope

### Tables taken from `db/reference/schema_import.sql`

| Reference table | Now | Notes |
|---|---|---|
| `tenants` | `tenants.tenants` | All columns kept. `status`, `slug`, `default_currency` got CHECK constraints |
| `platform_plans` | `tenants.platform_plans` | All columns kept. `monthly_price` + `currency` are mapped as one `Money` value |
| `tenant_plan_subscriptions` | `tenants.tenant_plan_subscriptions` | All columns kept. At most one current subscription per tenant (partial unique index) |
| `tenant_payment_accounts` | `tenants.tenant_payment_accounts` | All columns kept. One account per provider, one default per tenant |
| `tenant_members` | `tenants.tenant_members` | Planned for Team; moved here (see decision D1) |

All five tables are created **code-first**: C# entities + `IEntityTypeConfiguration<T>` classes → EF migration.
The SQL that EF can't express (cross-module foreign key, exclusion constraint, RLS policies, seed data) is
hand-written inside the same migration.

### Delivered

- Domain model with rules and unit tests.
- 23 HTTP routes (public, member, owner and platform admin).
- Tenant resolution middleware in the host pipeline.
- Row-Level Security on the three tenant-owned tables.
- Platform admin API for plans and businesses.
- Public contracts other modules will use (`ITenantDirectory`, `ITenantPlanLimits`, events).
- 64 unit tests, 21 integration tests (including SQL-level RLS tests), 5 architecture rules.

### Not delivered (and why)

| Item | Reason |
|---|---|
| `POST /tenant/plan/checkout` (pay for a SaaS plan) | Needs the Payments module (Paymob) |
| Trial-end enforcement | Needs the daily "platform billing" job (PRD background jobs) |
| Custom domains | Phase 3 in the plan. `settings.customDomain` exists but only the platform may set it |
| Client counts in `GET /tenant/plan` usage | Needs the Clients module |
| Invites, adding/removing team members, custom roles | Team module (next) |
| Generic isolation suite ("every endpoint × two tenants") | Built per module for now; the generic harness is still open |

---

## 3. Design decisions

**D1. `tenant_members` lives in the Tenants module, not Team.**
Tenant resolution reads memberships on every tenant-scoped request, and creating a business must insert the owner
membership in the same transaction as the tenant. `UnitOfWorkBehavior` allows a command to change only one
module, so both rows must belong to the same module. The architecture plan also says shared reference data
(`users`, `tenants`, `tenant_members`) belongs to Identity/Tenants. Team will own invites and custom roles and
will change members through a Tenants contract.

**D2. `tenants` and `platform_plans` are global (no RLS).**
A business is looked up by slug *before* any tenant is chosen (tenant resolution, the public page, slug check),
and the price list is public. The application layer guards these tables: members only ever load the tenant chosen
by `X-Tenant`, and the admin API needs the platform admin claim.

**D3. A user may read their own memberships in every tenant.**
"Which businesses do I belong to?" (`GET /me/tenants`) and resolution itself must read `tenant_members` before a
tenant is set. The RLS policy on `tenant_members` therefore allows reading rows where `user_id` equals
`app.user_id`. Writing always requires the current tenant.

**D4. Endpoints that run outside a tenant are marked `.WithoutTenant()`.**
The Vue app will send `X-Tenant` on every call. Endpoints such as `POST /tenants`, `/me/tenants`, `/public/*`,
`/plans` and `/admin/*` must work regardless, so the middleware skips them instead of resolving (and possibly
rejecting) the header.

**D5. Creating a business switches the request to the new tenant.**
`CreateTenantHandler` calls `TenantContext.Set(newTenantId)` after creating the tenant. From then on the
stamping interceptor fills `tenant_id` on the owner membership and trial subscription, and RLS allows inserting
them because `app.tenant_id` is set when `SaveChanges` opens its connection.

**D6. Slugs are DNS labels.**
3-63 characters, `a-z`, `0-9`, single hyphens inside, never `--` (blocks punycode `xn--` look-alikes), never one
of ~35 reserved platform names (`www`, `api`, `admin`, `billing`, ...). The slug can't be changed after creation
because it is the business's address. Slugs of deleted tenants stay taken.

**D7. Permissions come from a role map in code for now.**
`RolePermissions` implements the PRD roles table (owner / coach / assistant). Code checks permissions, never
roles, so when Team turns the map into data no endpoint changes.

**D8. Enums are stored as snake_case text with CHECK constraints.**
`PastDue` is stored as `'past_due'`, the same spelling as the JSON API. The allowed values in each CHECK
constraint are generated from the C# enum, so they can't drift.

**D9. The admin list sorts newest first, then by slug.**
EF can't translate `Guid` comparisons for keyset paging, so the tie-breaker is the (unique) slug. The cursor stays
opaque: `"{createdAt ticks}|{slug}"` inside the usual base64url cursor.

**D10. Default payout account uses a deferred exclusion constraint.**
Moving the default from account A to B updates two rows in one save. A normal partial unique index could fail
between the two UPDATE statements, so the rule is
`EXCLUDE USING btree (tenant_id WITH =) WHERE (is_default) DEFERRABLE INITIALLY DEFERRED`, checked at commit.

---

## 4. Data model

Schema `tenants`, created by migration `20261026172930_InitialTenants`. Every table also gets the module's own
`outbox_messages`, `inbox_messages` and `__ef_migrations`.

### `tenants.tenants` (global)

| Column | Type | Rules |
|---|---|---|
| `id` | uuid | UUID v7, set by the app |
| `name` | text | required, ≤ 200 |
| `slug` | varchar(255) | unique; CHECK `^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])$` and no `--` |
| `status` | text | `trial` · `active` · `past_due` · `suspended` · `cancelled` (default `trial`) |
| `plan_id` | uuid | FK → `platform_plans` (restrict) |
| `default_currency` | char(3) | CHECK `^[A-Z]{3}$`, default `EGP` |
| `timezone` | text | IANA id, default `Africa/Cairo` |
| `default_locale` | text | `ar` / `en`, default `ar` |
| `branding` | jsonb | `{ logo_url, cover_image_url, primary_color, accent_color, tagline }` |
| `settings` | jsonb | `{ contact_email, contact_phone, whats_app, instagram_url, website_url, custom_domain }` |
| `trial_ends_at` | timestamptz | creation + `Tenants:TrialDays` |
| `created_at`, `updated_at`, `deleted_at` | timestamptz | soft delete via `deleted_at` |

Indexes: unique `slug`, `created_at` (admin list), `plan_id`.

### `tenants.platform_plans` (global)

| Column | Type | Rules |
|---|---|---|
| `id` | uuid | |
| `code` | text | unique, 2-50 of `a-z 0-9 - _`, never changes |
| `name` | text | required, ≤ 100 |
| `monthly_price` | numeric(12,2) | CHECK ≥ 0 |
| `currency` | char(3) | default `USD` |
| `max_clients`, `max_coaches` | integer | null = unlimited; CHECK > 0 when set |
| `features` | jsonb | flags such as `{ "training": true, "custom_domain": false }`; a missing key is off |
| `is_active` | boolean | default true; inactive plans are hidden and can't start trials |

Seeded by the migration (`ON CONFLICT (code) DO NOTHING`). **Prices are placeholders.**

| Code | Price/month | Max clients | Max coaches | Features |
|---|---|---|---|---|
| `starter` (trial plan) | 19 USD | 25 | 1 | nutrition, chat |
| `pro` | 49 USD | 100 | 3 | + training, custom_branding |
| `studio` | 129 USD | unlimited | 10 | + custom_domain |

### `tenants.tenant_members` (RLS, own rows readable across tenants)

| Column | Type | Rules |
|---|---|---|
| `id`, `tenant_id`, `user_id` | uuid | FK `tenant_id` → `tenants` (cascade); FK `user_id` → `identity.users` (cascade, added in SQL) |
| `role` | text | `owner` · `coach` · `assistant` |
| `status` | text | `active` · `suspended` (default `active`) |
| `bio` | text | |

Indexes: unique `(tenant_id, user_id)`, `user_id`.

### `tenants.tenant_plan_subscriptions` (RLS)

| Column | Type | Rules |
|---|---|---|
| `tenant_id`, `plan_id` | uuid | FK tenant (cascade), FK plan (restrict) |
| `status` | text | `trialing` · `active` · `past_due` · `cancelled` · `expired` |
| `current_period_start`, `current_period_end` | timestamptz | CHECK end > start |
| `provider` | text | null or `paymob` · `fawry` · `stripe` |
| `provider_subscription_id` | text | |
| `cancel_at_period_end` | boolean | default false |

Unique partial index `ix_tenant_plan_subscriptions_current` on `tenant_id`
`WHERE status IN ('trialing','active','past_due')`: one current subscription per tenant.

### `tenants.tenant_payment_accounts` (RLS)

| Column | Type | Rules |
|---|---|---|
| `tenant_id` | uuid | FK tenant (cascade) |
| `provider` | text | `paymob` · `fawry` · `stripe`; unique per tenant |
| `provider_account_id` | text | the provider's public merchant id, ≤ 200. Never a secret |
| `status` | text | `pending` · `active` · `disabled` (default `pending`) |
| `is_default` | boolean | one per tenant (deferred exclusion constraint, D10) |

### Row-Level Security

Written by the new `RlsSql` helper (`Infrastructure.Common`) inside the migration:

```sql
-- tenant_plan_subscriptions, tenant_payment_accounts
ALTER TABLE tenants.<table> ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tenants.<table>
  USING      (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
  WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- tenant_members: the user may also READ their own rows in other tenants
CREATE POLICY tenant_isolation ON tenants.tenant_members
  USING      (tenant_id = <current tenant> OR user_id = NULLIF(current_setting('app.user_id', true), '')::uuid)
  WITH CHECK (tenant_id = <current tenant>);
```

`app_user` (the API) is subject to these policies; `app_migrator` owns the tables and `app_system` has
`BYPASSRLS` for future cross-tenant jobs.

---

## 5. Domain (`CoachDB.Tenants.Domain`)

| Type | Responsibility |
|---|---|
| `Slug` | Value object: normalizes (trim, lower-case) and validates; errors `slug_invalid`, `slug_reserved` |
| `Tenant` (aggregate) | `StartTrial(...)` sets trial status, plan, trial end and raises `TenantCreated`. `UpdateProfile` (null = unchanged), `UpdateBranding`, `UpdateSettings` (keeps the platform-set custom domain), `Suspend` / `Reactivate` (idempotent, raise `TenantSuspended` / `TenantReactivated`; reactivation returns to `trial` if the trial hasn't ended, else `active`). `IsAccessible` = not deleted, not suspended/cancelled |
| `TenantBranding`, `TenantSettings` | Immutable records stored as jsonb |
| `TenantMember` | Membership with `MemberRole` and `MemberStatus`; `CreateOwner(...)`; `CountsAsCoach` (owner or coach) |
| `PlatformPlan` (aggregate) | `Create` (code validated and normalized), `Update` (PUT semantics, features sorted), `SetActive`, `Has(feature)`. `PlanFeatures` holds the flag names the code checks |
| `TenantPlanSubscription` | `StartTrial(...)`, `IsCurrent` |
| `TenantPaymentAccount` | `Connect(...)` (starts `pending`), `MakeDefault` (refused when `disabled`), `ClearDefault` |
| `TenantsErrors` | Every expected failure with its stable code (section 8) |

---

## 6. Application (`CoachDB.Tenants.Application`)

Each use case is a command or query with its validator and handler in one file, following the slice template.

| Use case | Type | Authorization |
|---|---|---|
| `CreateTenantCommand` | command | signed in (handler check) |
| `CheckSlugQuery` | query | anonymous |
| `ListMyTenantsQuery` | query | signed in |
| `GetPublicTenantQuery` | query | anonymous |
| `GetCurrentTenantQuery` | query | `[RequiresTenant]` |
| `UpdateTenantCommand` | command | `[RequiresPermission(tenant.manage)]` |
| `ListPlansQuery` | query | anonymous |
| `GetTenantPlanQuery` | query | `[RequiresTenant]` |
| `ListPaymentAccountsQuery`, `GetPaymentAccountQuery`, `ConnectPaymentAccountCommand`, `MakeDefaultPaymentAccountCommand`, `RemovePaymentAccountCommand` | mixed | `[RequiresPermission(tenant.billing)]` |
| `ListAllPlansQuery`, `GetPlanQuery`, `CreatePlanCommand`, `UpdatePlanCommand`, `SetPlanActiveCommand` | mixed | `[RequiresPlatformAdmin]` |
| `ListTenantsQuery`, `GetTenantQuery`, `SuspendTenantCommand`, `ReactivateTenantCommand` | mixed | `[RequiresPlatformAdmin]` |

**`CreateTenantHandler` steps:**
1. Signed in, else `401 auth_required`.
2. Parse the slug (`slug_invalid` / `slug_reserved`).
3. The user owns fewer than `MaxOwnedTenantsPerUser` businesses, else `403 tenant_limit_reached`.
4. The slug is free (including deleted tenants), else `409 slug_taken`.
5. The trial plan (`TrialPlanCode`) exists and is active, else `500 trial_plan_unavailable`.
6. Create the tenant, switch the request to it (D5), add the owner membership and the `trialing` subscription.
7. `UnitOfWorkBehavior` saves all rows and the `TenantCreated` outbox message in one transaction.

**Validation rules** (`Validation/TenantRules.cs`; messages are error codes):

| Rule | Code |
|---|---|
| 3-letter currency | `invalid_currency` |
| IANA time zone known to the server (`Africa/Cairo`, `UTC`; Windows ids rejected) | `invalid_timezone` |
| `ar` / `en` | `unsupported_locale` |
| absolute `https://` URL ≤ 500 | `invalid_url` |
| `#RRGGBB` color | `invalid_color` |
| text ≤ 200 (phone fields ≤ 32) | `too_long` |
| plan price ≥ 0 / limits > 0 / feature keys snake_case, ≤ 50 keys | `must_not_be_negative` / `must_be_positive` / `invalid_feature_key`, `too_many` |

**Role → permission map** (`Authorization/RolePermissions.cs`):

| Permission | Owner | Coach | Assistant |
|---|---|---|---|
| `tenant.manage`, `tenant.billing` | ✓ | | |
| `team.read` | ✓ | ✓ | ✓ |
| `team.manage` | ✓ | | |
| `clients.read` | ✓ | ✓ | ✓ |
| `clients.read_all` | ✓ | | |
| `clients.write` | ✓ | ✓ | |
| `packages.write` | ✓ | ✓ | |
| `questionnaires.write` | ✓ | ✓ | ✓ |
| `plans.write` | ✓ | ✓ | |
| `plans.write_draft` | ✓ | ✓ | ✓ |
| `checkins.review` | ✓ | ✓ | |
| `chat.use` | ✓ | ✓ | ✓ |
| `revenue.read_all` | ✓ | | |
| `revenue.read_own` | ✓ | ✓ | |

---

## 7. HTTP API

All routes are under `/api/v1`, follow the README contract (plain DTOs, `CursorPage` for lists,
RFC 7807 problem details with a stable `code`), and appear in the OpenAPI document and Scalar.
Sample requests: [`src/Host/CoachDB.Api/http/tenants.http`](../src/Host/CoachDB.Api/http/tenants.http).

### 7.1 Routes

| Method & route | Who | Success | Main errors |
|---|---|---|---|
| `POST /tenants` | signed in | 201 + `Location: /api/v1/tenant`, `TenantResponse` | 400, 401, 403 `tenant_limit_reached`, 409 `slug_taken` |
| `GET /tenants/slug-availability?slug=` | anyone | 200 `{ slug, available, reason }` | — |
| `GET /me/tenants` | signed in | 200 `CursorPage<MyTenantResponse>` | 401 |
| `GET /public/{slug}` | anyone | 200 `PublicTenantResponse` | 404 `tenant_not_found` |
| `GET /tenant` | member | 200 `TenantResponse` | 400 `tenant_required`, 403, 404 |
| `PATCH /tenant` | `tenant.manage` | 200 `TenantResponse` | 400, 403 `permission_denied` |
| `GET /tenant/plan` | member | 200 `TenantPlanResponse` | 400, 403 |
| `GET /plans` | anyone | 200 `CursorPage<PlanResponse>` | — |
| `GET /tenant/payment-accounts` | `tenant.billing` | 200 `CursorPage<PaymentAccountResponse>` | 400, 403 |
| `POST /tenant/payment-accounts` | `tenant.billing` | 201 + `Location`, `PaymentAccountResponse` | 400, 409 `payment_account_exists` |
| `GET /tenant/payment-accounts/{id}` | `tenant.billing` | 200 | 404 `payment_account_not_found` |
| `POST /tenant/payment-accounts/{id}/default` | `tenant.billing` | 200 | 404, 409 `payment_account_disabled` |
| `DELETE /tenant/payment-accounts/{id}` | `tenant.billing` | 204 | 404 |
| `GET /admin/plans` | platform admin | 200 `CursorPage<PlanResponse>` (incl. inactive) | 401, 403 |
| `POST /admin/plans` | platform admin | 201 + `Location` | 400, 409 `plan_code_taken` |
| `GET /admin/plans/{id}` | platform admin | 200 | 404 `plan_not_found` |
| `PUT /admin/plans/{id}` | platform admin | 200 | 400, 404 |
| `POST /admin/plans/{id}/activate`, `/deactivate` | platform admin | 200 | 404 |
| `GET /admin/tenants?status=&search=&cursor=&limit=` | platform admin | 200 `CursorPage<TenantResponse>`, newest first | 400 (bad status / cursor / limit) |
| `GET /admin/tenants/{id}` | platform admin | 200 | 404 |
| `POST /admin/tenants/{id}/suspend`, `/reactivate` | platform admin | 200 | 404 |

"member" = signed in, `X-Tenant` names the business, and the user is an active member of it.

### 7.2 Examples

**Create a business**

```http
POST /api/v1/tenants
Authorization: Bearer <access token>
Content-Type: application/json

{ "name": "Fit With Ahmed", "slug": "fit-with-ahmed", "defaultCurrency": "EGP", "timezone": "Africa/Cairo", "defaultLocale": "ar" }
```

```http
HTTP/1.1 201 Created
Location: /api/v1/tenant

{
  "id": "01a1794f-bc7f-7b52-ad5b-fa3557e86383",
  "name": "Fit With Ahmed",
  "slug": "fit-with-ahmed",
  "status": "trial",
  "planId": "4e79c794-a606-4e79-9458-b44f836c5617",
  "defaultCurrency": "EGP",
  "timezone": "Africa/Cairo",
  "defaultLocale": "ar",
  "branding": { "logoUrl": null, "coverImageUrl": null, "primaryColor": null, "accentColor": null, "tagline": null },
  "settings": { "contactEmail": null, "contactPhone": null, "whatsApp": null, "instagramUrl": null, "websiteUrl": null, "customDomain": null },
  "trialEndsAt": "2026-11-09T17:43:51.1667238+00:00",
  "createdAt": "2026-10-26T17:43:51.1667238+00:00",
  "updatedAt": "2026-10-26T17:43:51.1667238+00:00"
}
```

Only `name` and `slug` are required; the region fields default to EGP / Africa/Cairo / ar.

**My businesses**

```json
GET /api/v1/me/tenants
{
  "items": [
    {
      "id": "01a1794f-…", "slug": "fit-with-ahmed", "name": "Fit With Ahmed", "status": "trial",
      "role": "owner",
      "permissions": ["tenant.manage", "tenant.billing", "team.read", "…"],
      "logoUrl": null
    }
  ],
  "nextCursor": null,
  "hasMore": false
}
```

**Plan and usage**

```json
GET /api/v1/tenant/plan   (X-Tenant: fit-with-ahmed)
{
  "plan": {
    "id": "4e79c794-…", "code": "starter", "name": "Starter",
    "monthlyPrice": { "amount": 19.00, "currency": "USD" },
    "maxClients": 25, "maxCoaches": 1,
    "features": { "chat": true, "training": false, "nutrition": true, "custom_domain": false, "custom_branding": false },
    "isActive": true
  },
  "subscription": {
    "id": "01a1794f-…", "status": "trialing",
    "currentPeriodStart": "2026-10-26T17:43:51.166723+00:00",
    "currentPeriodEnd": "2026-11-09T17:43:51.166723+00:00",
    "cancelAtPeriodEnd": false
  },
  "usage": { "coaches": 1, "maxCoaches": 1, "maxClients": 25 }
}
```

**Partial update** (fields left out stay unchanged; `branding` and `settings` replace the whole object)

```http
PATCH /api/v1/tenant
X-Tenant: fit-with-ahmed

{
  "name": "Ahmed Coaching",
  "branding": { "logoUrl": "https://cdn.example.com/logo.png", "primaryColor": "#0F766E", "tagline": "Get strong" },
  "settings": { "contactEmail": "Hello@Example.com", "whatsApp": "+201000000000" }
}
```

The response is the updated `TenantResponse`. Colors and email are lower-cased, blank strings become `null`,
and a `customDomain` in the body is ignored.

**Validation error**

```json
POST /api/v1/tenants  { "name": "", "slug": "admin", "timezone": "Mars/Olympus" }
HTTP/1.1 400
{
  "type": "https://coachdb.app/errors/validation_failed",
  "title": "One or more fields are invalid.",
  "status": 400,
  "code": "validation_failed",
  "traceId": "…",
  "errors": { "name": ["required"], "slug": ["slug_reserved"], "timezone": ["invalid_timezone"] }
}
```

**No tenant chosen**

```json
GET /api/v1/tenant   (no X-Tenant)
HTTP/1.1 400
{ "type": "https://coachdb.app/errors/tenant_required", "title": "Choose a business: send the X-Tenant header with its slug.",
  "status": 400, "code": "tenant_required", "traceId": "…" }
```

---

## 8. Error codes added

| Code | HTTP | When |
|---|---|---|
| `tenant_required` | 400 | A tenant-scoped use case ran without `X-Tenant` / subdomain |
| `tenant_not_found` | 404 | Unknown or malformed slug, deleted tenant, suspended tenant on the public page |
| `tenant_suspended` | 403 | The chosen business is suspended or cancelled |
| `tenant_access_denied` | 403 | Signed in but not an active member of the chosen business |
| `slug_invalid` / `slug_reserved` | 400 (field error) | Slug rules (D6) |
| `slug_taken` | 409 | Slug already used, including by a deleted business |
| `tenant_limit_reached` | 403 | The user already owns `MaxOwnedTenantsPerUser` businesses |
| `trial_plan_unavailable` | 500 | `Tenants:TrialPlanCode` missing or inactive (configuration error) |
| `plan_not_found` | 404 | |
| `plan_code_taken` | 409 | |
| `plan_code_invalid` | 400 (field error) | |
| `payment_account_not_found` | 404 | Also for another business's account id |
| `payment_account_exists` | 409 | The provider is already connected |
| `payment_account_disabled` | 409 | Making a disabled account the default |

---

## 9. Tenant resolution (Phase 0 step 6)

`TenantResolutionMiddleware` (`Api.Common/Context`) runs after authentication and before authorization:

```
request
  ├─ endpoint marked .WithoutTenant()? ─────────────────────────► continue (header ignored)
  ├─ slug from subdomain of Tenancy:RootDomain, else X-Tenant
  │    └─ none ─────────────────────────────────────────────────► continue (no tenant)
  ├─ malformed slug ──────────────────────────────────────────────► 404 tenant_not_found
  ├─ ITenantResolver (Tenants module)
  │    ├─ unknown slug ───────────────────────────────────────────► 404 tenant_not_found
  │    ├─ suspended / cancelled ──────────────────────────────────► 403 tenant_suspended
  │    ├─ anonymous ──► tenant set, no permissions
  │    ├─ not an active member ───────────────────────────────────► 403 tenant_access_denied
  │    └─ member ─────► tenant set + role + permissions
  └─ TenantContext.Set(id, slug); permissions added as "perm" claims; TenantId added to the log scope
```

`AuthorizationBehavior` then checks the message's attributes in a fixed order:
1. `401 auth_required`
2. `403 permission_denied` for `[RequiresPlatformAdmin]` without the `padm` claim
3. `400 tenant_required` when `[RequiresTenant]` or any `[RequiresPermission]` and no tenant
4. `403 permission_denied` for a missing permission

Clients (customers of a business) are not members. The resolver will also accept them once the Clients module
exists.

---

## 10. Shared building blocks changed

| File | Change |
|---|---|
| `Application.Abstractions/Authorization/Permissions.cs` | New `[RequiresTenant]` and `[RequiresPlatformAdmin]` attributes |
| `Application.Abstractions/Behaviors/AuthorizationBehavior.cs` | Takes `ITenantContext`; checks admin, tenant, then permissions (section 9) |
| `Application.Abstractions/Context/ITenantResolver.cs` | New port + `ResolvedTenant` + shared `TenancyErrors` |
| `Api.Common/Context/TenantResolutionMiddleware.cs` | New middleware, `TenancyOptions`, `.WithoutTenant()` convention |
| `Api.Common/Context/ContextSetup.cs` | Binds the `Tenancy` options |
| `Infrastructure.Common/Persistence/Interceptors.cs` | RLS interceptor also sets `app.user_id`; timestamps interceptor keeps an `UpdatedAt` the domain already set |
| `Infrastructure.Common/Persistence/RlsSql.cs` | New: SQL for `tenant_isolation` policies, reused by every future module |
| `Infrastructure.Common/Persistence/PropertyBuilderExtensions.cs` | New: `HasSnakeCaseConversion()` (enum ↔ snake_case text, nullable too), `HasSnakeCaseDefault()`, `SqlValues<TEnum>()` for CHECKs, `HasJsonbConversion()` for value objects |
| `Host/CoachDB.Api/Program.cs` | `AddTenantsModule().AddTenantsEndpoints()`; `UseMiddleware<TenantResolutionMiddleware>()` |
| `Host/CoachDB.Migrator/MigrationRunner.cs` | Migrates `TenantsDbContext` after Identity |
| `deploy/Dockerfile` | Runtime image changed to `aspnet:10.0-noble-chiseled-extra` (ICU + tzdata, needed for IANA time zones). **Not yet built** |
| `.github/workflows/ci.yml` | Migration drift check for the Tenants module |

---

## 11. Configuration

`src/Host/CoachDB.Api/appsettings.json`:

```json
"Tenancy": { "RootDomain": "" },
"Tenants": { "TrialDays": 14, "TrialPlanCode": "starter", "MaxOwnedTenantsPerUser": 3 }
```

- `Tenancy:RootDomain`: set to `coachdb.app` in production so `fitwithahmed.coachdb.app` resolves the tenant.
  The platform subdomains `www`, `api`, `app` and `admin` are never treated as tenants. Empty means header only
  (local development).
- CORS already allows any header, so browsers can send `X-Tenant`.

---

## 12. Tests

| Project | New tests | What they prove |
|---|---|---|
| `tests/Modules/CoachDB.Tenants.UnitTests` (new) | 64 | Slug rules, tenant lifecycle and events, plan rules, billing entities, every validator, role map, admin cursor |
| `tests/CoachDB.IntegrationTests/Tenants/TenantFlowTests` | 10 | Create → trial → owner → `/me/tenants` → `/tenant` → `/tenant/plan`; validation codes; slug uniqueness; resolution errors; header ignored on agnostic routes; PATCH; public profile; owner limit; sign-in required; plan list order |
| `…/Tenants/BillingAndAdminTests` | 7 | Payout accounts and moving the default; another business gets 404 on every account route; coach is refused billing and settings but may read; admin access control; plan CRUD + validation; admin paging across pages; suspend → members locked out → reactivate |
| `…/Tenants/RowLevelSecurityTests` | 4 | As `app_user` in SQL: only the current tenant's rows; no tenant → no rows; inserting into another tenant fails with `42501`; a user sees only their own memberships across tenants |
| `tests/CoachDB.BuildingBlocks.UnitTests` | +3 | `tenant_required`, `[RequiresTenant]`, `[RequiresPlatformAdmin]` |
| `tests/CoachDB.ArchitectureTests` | +5 | Layer rules for the five Tenants projects |

Totals now: **246 test cases, 0 failures** (BuildingBlocks 57, Identity 61, Tenants 64, Api 13, Architecture 16,
Integration 35).

Also verified by hand against the local database: create → plan → `/me/tenants` → `tenant_required`, and the
OpenAPI document lists all 23 new paths.

---

## 13. Files

```
src/Modules/Tenants/
  CoachDB.Tenants.Contracts/
    ITenantDirectory.cs              ITenantDirectory + TenantSummary, ITenantPlanLimits + PlanLimits
    Events/TenantEvents.cs           TenantCreated, TenantSuspended, TenantReactivated
  CoachDB.Tenants.Domain/
    TenantsErrors.cs
    Tenants/Slug.cs, Tenants/Tenant.cs        (+ TenantStatus, TenantBranding, TenantSettings)
    Members/TenantMember.cs                   (+ MemberRole, MemberStatus)
    Plans/PlatformPlan.cs                     (+ PlanFeatures)
    Billing/TenantPlanSubscription.cs         (+ PlanSubscriptionStatus, PaymentProvider)
    Billing/TenantPaymentAccount.cs           (+ PaymentAccountStatus)
  CoachDB.Tenants.Application/
    TenantsOptions.cs
    Abstractions/Ports.cs                     repositories
    Authorization/RolePermissions.cs
    Validation/TenantRules.cs
    Tenants/TenantResponse.cs, Tenants/Queries.cs, Tenants/Create/…, Tenants/Update/…
    Plans/PlanQueries.cs
    PaymentAccounts/PaymentAccounts.cs
    Admin/AdminPlans.cs, Admin/AdminTenants.cs
  CoachDB.Tenants.Infrastructure/
    TenantsModule.cs                          DI registration
    Persistence/TenantsDbContext.cs, Configurations.cs, Repositories.cs, DesignTimeFactory.cs
    Persistence/Migrations/20261026172930_InitialTenants.cs (+ Designer, snapshot)
    Resolution/TenantResolver.cs              ITenantResolver, ITenantDirectory, ITenantPlanLimits
  CoachDB.Tenants.Api/
    TenantsEndpoints.cs                       AddTenantsEndpoints, tags, snake_case query enums
    Tenants/TenantEndpoints.cs, Billing/BillingEndpoints.cs, Admin/AdminEndpoints.cs
src/Host/CoachDB.Api/http/tenants.http
tests/Modules/CoachDB.Tenants.UnitTests/…
tests/CoachDB.IntegrationTests/Session.cs, Tenants/…
```

---

## 14. How to try it

```bash
docker compose -f deploy/docker-compose.yml up -d
dotnet run --project src/Host/CoachDB.Migrator      # applies InitialTenants + seeds the plans
dotnet run --project src/Host/CoachDB.Api           # http://localhost:5240/scalar
```

1. `POST /api/v1/auth/register`, then copy `accessToken`.
2. `POST /api/v1/tenants` with a name and a slug.
3. Send `X-Tenant: <slug>` on `GET /api/v1/tenant` and `GET /api/v1/tenant/plan`.
4. For the admin API, run `UPDATE identity.users SET is_platform_admin = true WHERE email = '…';` and sign in
   again (the `padm` claim is in the token).

---

## 15. Known limits and follow-ups

- **Race on the slug:** two simultaneous creates with the same slug both pass the check; the unique index rejects
  the second with a 500 instead of `409 slug_taken`. Rare, but a unique-violation → 409 mapping would fix it.
- **Resolution cost:** two small queries per tenant-scoped request. Cache the slug → tenant lookup and the
  permission map (HybridCache) when traffic grows.
- **Plan IDs** seeded by SQL are UUID v4 (`gen_random_uuid()`); everything created by the app is v7. Harmless.
- **Admin search** uses `LIKE` on the lower-cased name; add a trigram index when there are many tenants.
- **Suspended owners** can't reach even billing. Revisit when plan checkout exists, so they can pay to reactivate.

## 16. Next

**Team module** (Phase 1, module 2): invites by email (`team_invites`), accepting an invite, listing, changing
and removing members, custom roles with a role → permission table (`role_permissions`), and enforcing the plan's
`maxCoaches` through `ITenantPlanLimits`.
