# PR draft — feat/billing-invoicing → develop

**Title:** `feat(billing): immutable invoices, proration and invoice endpoints`

## Summary
Implements spec 04: an immutable `Invoice` aggregate (`TrialConversion`, `Renewal`, `Upgrade`, `Reactivation`) with lines whose sum always equals the total, numbered `SUB-{yyyy}-{000001}` from the Postgres sequence `billing.invoice_number_seq`. `ProrationCalculator` prorates by seconds with half-up rounding in `long` cents (Int128 inside `Money.Fraction`). Invoices are issued in the same transaction as the subscription transition: trial conversion (`TrialExpiryJob`), renewal (`PeriodEndJob`), upgrade (`change-plan`, which now returns `invoiceId`) and reactivation. `SuspensionExpiryJob` voids the open invoice with `suspension-expired`. Endpoints: `GET /v1/invoices` (cursor, newest first), `GET /v1/invoices/{id}` with lines (TenantAdmin), `POST /admin/invoices/{id}/void` (PlatformAdmin, reason required). `IBillingApi` gains `GetInvoiceAsync`, `ListOpenInvoicesAsync` and an idempotent `MarkInvoicePaidAsync`.

## Notable Decisions
- Upgrade invoices cover the prorated remainder `[now, period end)`, so `UNIQUE(subscription_id, period_start, reason)` still allows two upgrades in one period while blocking duplicated renewals.
- Monthly → annual: credit for the unused month + the full annual price; the new annual period starts now.
- A total ≤ 0 is recorded as `Paid` with total 0; a negative sum gets an adjustment line (no carried-over credit) so the total still equals the lines.
- `MarkInvoicePaid` on a voided invoice is ignored with a reconciliation warning.
- Cursor = `(issued_at, number)` encoded in base64url.

## Test Plan
- Unit: proration table (start, middle, end, half-up rounding, after the end, monthly → annual) and properties (credit ≤ old price, charge ≥ credit); invoice issue/zero total/negative sum/idempotent payment/void rules.
- Integration: conversion invoice + `InvoiceIssued`; prorated upgrade lines; one renewal per period with the unique index rejecting a duplicate (23505); cursor pagination + invalid cursor 400; Member 403 and cross-tenant 404; void RBAC, reason validation, 409 on paid invoices; `MarkInvoicePaid` twice = one `InvoicePaid`; suspension expiry voids the open invoice.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
