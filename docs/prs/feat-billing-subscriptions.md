# PR draft — feat/billing-subscriptions → develop

**Title:** `feat(billing): subscription lifecycle, plan changes and lifecycle jobs`

## Summary
Implements spec 03 in the Billing module: a `Subscription` aggregate that owns every status transition (trial, conversion, expiry, upgrade/downgrade/lateral change, cancel/resume, period close, past due, suspension, suspension expiry, reactivation), each returning `Result` and publishing an outbox event. `TenantProvisioned` starts a 14-day trial (idempotent). Endpoints: `GET /v1/subscription`, `POST /v1/subscription/{change-plan,cancel,resume,reactivate}`, `DELETE /v1/subscription/pending-change`, `GET /admin/subscriptions?status=`. Hangfire jobs: `TrialEndingNotifierJob` (daily), `TrialExpiryJob` and `PeriodEndJob` (hourly), `SuspensionExpiryJob` (daily), batched 100 at a time with one transaction per subscription. Consumers for `InvoicePaid`, `PaymentFailed` and `PaymentRetriesExhausted`. `IBillingApi.GetEntitlementsAsync` is now real and cached with HybridCache (tag invalidation on every subscription write) — closes 02-T05.

## Notable Decisions
- Billing periods are `anchor + k intervals` (extra `billing_anchor` column) so the original day is kept (31/01 → 28/02 → 31/03).
- Upgrade/downgrade: an interval change decides on its own (monthly → annual = upgrade); within the same interval the prices are compared (the duration cancels out).
- Downgrade seat check uses occupied seats (active users + pending invitations), consistent with the seat policy.
- `trial_ending_notified_at` guarantees a single D-3 notice per subscription.
- Optimistic concurrency via the `xmin` system column → `409 concurrency-conflict`.
- New `Payments.Contracts` project (`IPaymentsApi`, `PaymentFailed`, `PaymentRetriesExhausted`); the payment-method check returns `false` until spec 05.
- Invoice issuance (TrialConversion/Upgrade/Renewal/Reactivation) is wired in spec 04 (task 04-T04).

## Test Plan
- Unit: every valid and invalid transition, classifier, billing period table tests (month-end and leap-year anchors).
- Integration: signup → Trialing; duplicated `TenantProvisioned`; trial → Expired / Active; D-3 notice once; change plan during trial; upgrade invalidates entitlements; downgrade scheduled → applied by `PeriodEndJob` (double run = no-op); 409 `seats-exceed-target-plan`; cancel pending change; cancel/resume/period-end cancel; reactivate (402 without payment method); PastDue → Suspended → Active → 30-day expiry; concurrent writes → 409; Member read-only, tenant isolation, PlatformAdmin-only listing.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
