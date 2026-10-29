# PR draft — feat/payments-webhooks → develop

**Title:** `feat(payments): fake gateway, signed webhooks, idempotent charges and dunning`

## Summary
Implements spec 05 in the Payments module (schema `payments`: `payment_methods`, `charges`, `payment_events`, `dunning`). `InvoiceIssued` with total > 0 and a payment method creates charge attempt 1 (`idempotency_key = {invoiceId}:{attempt}`) and calls the gateway through a typed `HttpClient` with the standard resilience handler (3 s per attempt, 10 s total, jittered retries, circuit breaker) that resends the same `Idempotency-Key`. The `Fake` gateway is served by the API itself, decides by test token (`pm_card_ok`, `pm_card_declined`, `pm_card_insufficient_funds`, `pm_card_fail_then_ok`) and schedules (1–3 s, Hangfire) a real HTTP webhook signed with `X-Subscribely-Signature: t=…,v1=HMAC-SHA256`. `POST /webhooks/payments/fake` verifies the signature in constant time with a 5-minute tolerance (401 otherwise), inserts into `payment_events` first (UNIQUE provider + event id → duplicates return 200 with no effect) and enqueues `ProcessPaymentEventJob`, which ignores events for terminal charges, rejects amount mismatches, marks invoices paid via `IBillingApi` and drives the dunning. `DunningJob` (hourly) retries at D+1, D+3 and D+7 from issue; failures publish `PaymentFailed`, the last one `PaymentRetriesExhausted`. Endpoints: `GET/PUT /v1/billing/payment-method` (immediate retry when PastDue/Suspended), `POST /v1/invoices/{id}/pay` (409 while a charge is pending), `POST /dev/fake-gateway/events/{id}/replay`. `IPaymentsApi.HasDefaultPaymentMethodAsync` is now real.

## Notable Decisions
- The fake gateway is reached over HTTP (not in-process) so retries, idempotency keys and webhook signatures are exercised for real; in tests every `HttpClient` uses the `TestServer` handler.
- A partial unique index allows at most one `Pending` charge per invoice.
- Manual attempts (pay now / new card) do not consume the dunning schedule (`charges.manual`).
- A webhook that arrives before its charge is saved throws and is retried by Hangfire with backoff (5×).
- Replay is mapped in Development and in the `Testing` environment.

## Test Plan
- Unit: signature (valid, expired, future, tampered, other secret, malformed headers, empty secret), dunning schedule and events, fake token decisions, idempotency key format.
- Integration: `pm_card_ok` conversion paid; no payment method → no charge; `pm_card_declined` D+1/D+3/D+7 → Suspended, then a new card reactivates; `pm_card_fail_then_ok` recovers on D+1; replayed webhook = one event and one `InvoicePaid`; bad/expired/tampered signatures → 401; failure after success → `Ignored`; amount mismatch → `Rejected`; pay now 202/409/404 across tenants; payment method RBAC and validation.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
