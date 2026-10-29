# PR draft — test/e2e-scenarios → develop

**Title:** `test(e2e): end-to-end journeys, coverage gate and final CI`

## Summary
Implements spec 09. A small scenario DSL (`Journey` for the tenant acting through the HTTP API, `Clock` to travel in time and let jobs, outbox and gateway webhooks settle, `MailpitClient` for e-mails) and the ten end-to-end journeys: happy path, prorated upgrade with the new quota, seat-blocked then scheduled downgrade, cancel/resume/cancel → 402, dunning D+1/D+3/D+7 → Suspended → new card → Active, webhook replayed 3× with a single effect, tenant isolation over JWT and API key, quota 429 with headers and one-time admin-only alerts, 30-day suspension → Canceled + Void → reactivation keeping the data, password reset by e-mail revoking the old refresh token. Extra payment edge cases (unknown provider, malformed body, webhook before the charge, unknown event type, success for a voided invoice, dunning on a non-open invoice, gateway down, `Provider = None`). Coverage moves from `coverlet.collector` (VSTest only) to `Microsoft.Testing.Extensions.CodeCoverage`; ReportGenerator merges unit + integration and `scripts/check-coverage.py` enforces Billing/Payments ≥ 90% and 100% on `ProrationCalculator`, `Subscription` and `WebhookSignature`. The CI publishes the summary and the HTML report as an artifact; the compose smoke waits ≤ 60 s for `/health/ready`.

## Notable Decisions
- Generated code (logging source generators, OpenAPI) and EF migrations are excluded from coverage.
- Scenario 6 reads the gateway event id from the database: no product API exposes it.
- `TestTokens.Issue` accepts the tenant slug so long-lived admin tokens can reach the data plane.

## Test Plan
- Local: unit 182 · architecture 17 · integration 121 (10 E2E) all green; coverage total 93.0%, Billing 93.1%, Payments 96.2%, critical classes 100%; gate script exits 0.
- Local compose smoke: stack ready in ~4 s; signup → provisioning → Trialing → API key → `POST /v1/projects` 201 with rate-limit headers → payment method → usage.
- GitHub Actions not run yet (no remote configured).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
