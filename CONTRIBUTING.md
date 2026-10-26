# Contributing to CoachDB

Analyzers, `.editorconfig` and architecture tests enforce as many of these rules as they can, which leaves code review free to focus on logic.

## Code

- Classes are `sealed` and `internal` by default. Only `Contracts` and the module registration/endpoint entry points are `public`.
- Commands, queries, DTOs and events are records. Handlers use primary constructors.
- Nullable reference types are on. Don't use `!` without a comment explaining why.
- Every async method takes a `CancellationToken` and passes it on.
- Domain code never calls `DateTime.Now` or `Guid.NewGuid()`. Use `TimeProvider`/`IClock`, and `Guid.CreateVersion7()` for new keys.
- Expected failures return `Result`/`Error`. Exceptions are only for bugs and infrastructure failures.
- Entities have private setters, and state changes only through methods that enforce the rules.
- Endpoints, EF configurations and jobs contain no business logic.
- API responses are DTOs, never entities.
- A module never reads another module's tables. Modules talk through `*.Contracts` interfaces or integration events.

## Naming

| Thing | Pattern | Example |
|---|---|---|
| Command | `<Verb><Noun>Command` | `SubmitCheckInCommand` |
| Query | `Get<Noun>Query` / `List<Nouns>Query` | `ListClientsQuery` |
| Handler | `<Command/Query name minus suffix>Handler` | `SubmitCheckInHandler` |
| Validator | `<Command/Query name minus suffix>Validator` | `SubmitCheckInValidator` |
| Endpoint | `<Verb><Noun>Endpoint` | `SubmitCheckInEndpoint` |
| Request / response | `<Verb><Noun>Request` / `<Noun>Response` | `SubmitCheckInRequest`, `CheckInResponse` |
| Domain event | past tense | `CheckInSubmitted` |
| Error code | `snake_case`, in a module-scoped `<Noun>Errors` class | `CheckInErrors.AlreadyReviewed` → `checkin_already_reviewed` |
| Permission | `<area>.<action>` | `plans.write`, `checkins.review` |
| Database | `snake_case`, plural tables | `check_ins.submitted_at` |

## Git and review

- Work trunk-based: short-lived branches, PRs under ~400 changed lines, squash merge.
- Use Conventional Commits, for example `feat(payments): add fawry refunds`, `fix(clients): …`, `chore(ci): …`.
- An endpoint PR also adds the endpoint's validator, its tests and its tenant-isolation case.
- Record significant decisions as ADRs in `docs/adr/`.
- Payments and Identity changes need a second reviewer.

## Definition of done (per feature)

- [ ] Command/query, handler, validator and endpoint sit together in one slice folder
- [ ] Domain rules are unit-tested; the handler is integration-tested against Postgres (Testcontainers)
- [ ] The permission is declared and the tenant isolation suite passes
- [ ] Error codes are documented in OpenAPI and the Vue client is regenerated
- [ ] Logs and audit entries are checked, with no tokens, passwords, card data or medical notes logged
- [ ] Schema changes are additive (expand → migrate → contract)
- [ ] `dotnet build` (warnings as errors), `dotnet format --verify-no-changes` and `dotnet test` are green
