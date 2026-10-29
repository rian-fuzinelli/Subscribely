# PR draft — chore/foundation-modular-monolith → develop

**Title:** `chore: foundation for the modular monolith (infrastructure, outbox, jobs, docker)`

## Summary
Sets up the Subscribely solution as a modular monolith: SharedKernel primitives (Result, Error, Entity, Money in cents), seven module projects with boundary tests, and the shared infrastructure — module DbContexts on the catalog database (schema per module, snake_case), tenant context and per-tenant connection factory, a transactional outbox with a polling dispatcher, Hangfire on PostgreSQL with an admin-only dashboard, ProblemDetails, request validation, correlation ids, JSON logs, health checks, OpenAPI + Scalar, and a docker compose stack (api, postgres, redis, mailpit).

## Notable Decisions
- One project per module; everything `internal` except `Contracts` and the module registration (enforced by NetArchTest).
- Outbox dispatched by a `BackgroundService` polling every 2s instead of a Hangfire recurring job (Hangfire's minimum cron granularity is 1 minute).
- DataAnnotations endpoint filter instead of the .NET 10 `AddValidation()` source generator, because endpoints live in class libraries.
- Configuration values are read lazily (options / service provider) so `WebApplicationFactory` overrides apply.
- Hangfire server started by a custom hosted service so it can be toggled per environment (`Jobs:ServerEnabled`).
- Newtonsoft.Json pinned transitively (13.0.4) to fix a vulnerable version pulled by Hangfire.

## Test Plan
- `dotnet test --project tests/Subscribely.UnitTests` → 39 passed.
- `dotnet test --project tests/Subscribely.ArchitectureTests` → 15 passed.
- `dotnet test --project tests/Subscribely.IntegrationTests` → 13 passed (health, outbox atomicity/retries, Hangfire execution, ProblemDetails, correlation id).
- `docker compose up -d --build` → `/health/ready` 200, `/health/live` 200, `/openapi/v1.json` 200, `/scalar` available, `/hangfire` 401 anonymous.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
