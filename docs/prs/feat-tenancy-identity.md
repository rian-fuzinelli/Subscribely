# PR draft — feat/tenancy-identity → develop

**Title:** `feat(tenancy): self-service signup, tenant provisioning, jwt auth, seats and api keys`

## Summary
Implements the Tenancy module: self-service signup (202) that provisions a dedicated PostgreSQL database per tenant through an outbox event + Hangfire job (idempotent, 3 attempts → `ProvisioningFailed`); JWT access tokens (15 min) with rotating refresh tokens and family revocation on reuse; logout; password reset by e-mail token (1 h, generic 202, revokes sessions); Redis-backed login rate limit (5/min per IP+e-mail); invitations with a seat policy (active users + pending invitations ≤ plan seats); user disable/enable with last-admin protection; API keys (`sk_live_{prefix}_{secret}`, SHA-256, shown once, cached 60 s, revocation invalidates the cache) authenticated by a dedicated scheme; tenant info and PlatformAdmin tenant listing; development PlatformAdmin seed.

## Notable Decisions
- `Billing.Contracts` and `Tenancy.Contracts` became separate projects to avoid a circular reference between modules.
- Global EF query filter on tenant-owned tables: when a tenant is in context only its rows are visible; anonymous/system/platform flows see all (explicit `IgnoreQueryFilters` only in `TenancyApi`).
- Default authentication is a policy scheme (API key when `X-Api-Key` is present, JWT otherwise); each endpoint policy pins the accepted scheme.
- JWT lifetime validation uses the injected `TimeProvider` (same clock that issues tokens).
- Rate limiting implemented with a Redis fixed window (fail-open) because ASP.NET's limiter cannot partition by request body.
- Integration tests now use an isolated catalog database per `WebApplicationFactory`, removing cross-test interference through the shared outbox.

## Test Plan
- Signup → provisioning → active tenant (database exists, `TenantProvisioned` emitted); provisioning idempotent; 3 failures → `ProvisioningFailed`.
- 409 slug/e-mail, 422 unknown plan, 400 invalid body; login before provisioning → 403.
- Login generic 401, 429 after 5 attempts, refresh rotation + reuse revokes family, expiry, logout, password reset end to end.
- Seats: limit with details, revoked/expired invitations free seats, accept flow (410 on reuse/expiry), member forbidden to invite, last admin 409.
- API keys: authentication sets tenant/actor, revocation immediate, rejected on JWT endpoints and in query string.
- Tenant isolation A×B across users, invitations, API keys, tenant info (404 on cross-tenant writes).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
