# PR draft — feat/audit-trail → develop

**Title:** `feat(audit): append-only audit trail with automatic entity auditing`

## Summary
Adds an audit trail written in the same transaction as business changes: an EF interceptor records `[Auditable]` entity changes (before/after JSON, excluding `[NotAudited]` fields) plus explicit actions buffered through `IAuditLog.Record`; `IAuditLog.WriteAsync` writes immediately for actions without state changes. Entries carry actor (user, API key, system job, webhook), tenant, correlation id and IP. `audit.entries` is append-only (trigger + revoked privileges). Query endpoints: `GET /v1/audit` (TenantAdmin, own tenant, cursor pagination) and `GET /admin/audit` (PlatformAdmin).

## Notable Decisions
- Interceptor in the same transaction (rejected: async via outbox) so nothing changes without a trail.
- Trigger + REVOKE as defense in depth.
- Separate control-plane (catalog) and data-plane (tenant DB) tables; the product source plugs in via `IProductAuditSource` (spec 06).
- Architecture test enforces `[NotAudited]` on any hash/token/secret/password property.

## Test Plan
- Integration: API change audited with actor and correlation; explicit record flushed on SaveChanges; audit failure rolls back the business change; UPDATE/DELETE rejected by trigger; tenant isolation + pagination; Member forbidden; admin filter.
- Architecture: sensitive field rule (+ negative control).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
