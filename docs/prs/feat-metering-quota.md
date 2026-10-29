# PR draft — feat/metering-quota → develop

**Title:** `feat(metering): monthly request quota, product gate and the Projects demo product`

## Summary
Implements spec 06. **Projects** (data plane): `/v1/projects` CRUD authenticated only by API key and persisted in the tenant database (`ProjectsDbContext` registered with the new `AddTenantDbContext<T>()`), product audit in `public.audit_entries` of the tenant database (now readable through `/v1/audit?source=product`), migrations applied on provisioning (`ITenantDatabaseMigrator`) and to every existing tenant on startup. **Metering**: product gate run before every product handler — subscription status (402 `subscription-inactive`, no quota consumed) → atomic Lua check-and-increment on `quota:{tenantId}:{yyyyMM}` (35-day TTL; rejected requests do not increment) → 429 `quota-exceeded` with `Retry-After`; `X-RateLimit-Limit/Remaining/Reset` on product responses; `QuotaThresholdReached` at 80% and 100% exactly once per period (`SET NX`); fail-open with a warning and the `quota_check_failed` metric when Redis is down; `GET /v1/usage`, `GET /v1/usage/history` (last 12 months) and the hourly `UsageSnapshotJob` into `metering.usage_periods`.

## Notable Decisions
- The gate is an `IProductRequestGate` endpoint filter declared in Infrastructure and implemented by Metering, so the product module never references another module.
- The snapshot never lowers a stored value (`GREATEST`), so a lost Redis key does not erase history.
- Entitlements cache invalidation failures (Redis down) are logged instead of failing the write that already committed.
- `Metering:Enabled = false` disables enforcement as a rollback switch.

## Test Plan
- Unit: UTC month window and reset, key formats, `Retry-After` rounding, 80/100% thresholds, remaining never negative.
- Integration: Projects CRUD in the tenant DB with product audit; JWT → 401 on `/v1/projects`; rate-limit headers; **200 concurrent requests with limit 100 → exactly 100 × 200 and 100 × 429**; 402 without consuming quota; alerts once per threshold; mid-month upgrade raises the limit keeping the counter; usage/history via the snapshot; startup migration repairs an existing tenant database; Redis down → requests pass and a warning is logged.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
