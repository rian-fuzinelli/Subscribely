# PR draft — feat/billing-plans → develop

**Title:** `feat(billing): versioned plan catalog with admin endpoints`

## Summary
Adds the Billing module persistence (`billing` schema) with versioned plans: PlatformAdmin creates, revises (new immutable version) and archives plans; `GET /v1/plans` lists active plans anonymously; `IBillingApi.GetActivePlanVersionAsync` exposes the current version to other modules; Development seeds starter/pro/business. Also adds JWT bearer validation and RBAC policies shared by all modules, and infrastructure fixes found while testing.

## Notable Decisions
- Plan revisions create a new `plan_versions` row (immutable); subscribers migrate on renewal (spec 03).
- Plan codes are validated lowercase (`^[a-z0-9-]{2,30}$`), so no `citext` is needed.
- `GetEntitlements` moved to spec 03, since entitlements depend on the subscription.
- Guid keys are client generated; an EF convention marks them `ValueGeneratedNever`.

## Test Plan
- Unit: plan creation, revision, price/limit validation, archive (`PlanTests`).
- Integration: create/list/duplicate/422/400/update/archive/404 with RBAC (401/403) (`PlanEndpointsTests`); full suite green 3 runs in a row.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
