# PR draft — feat/email-notifications → develop

**Title:** `feat(notifications): deduplicated pt-BR emails over SMTP for billing, payment, quota and identity events`

## Summary
Implements spec 07 in the Notifications module. Outbox consumers for `TrialEnding`, `InvoiceIssued`/`InvoicePaid` (total > 0), `PaymentFailed`, `SubscriptionSuspended` and `QuotaThresholdReached` email every active TenantAdmin (Members never receive quota alerts); `UserInvited` goes to the invitee and `PasswordResetRequested` to the user, carrying only the single-use link. Each recipient is reserved in `notifications.email_log` with a UNIQUE `dedup_key` (`…:{userId}` per admin) before sending, so reprocessing never resends. Sending uses MailKit (10 s timeout) to Mailpit in compose; an SMTP failure marks the row `Failed` and throws so the outbox retries, stopping after 5 attempts. Nine embedded templates (HTML + text, subject on the first text line) with `{{placeholders}}` always HTML-encoded, money as `R$ 1.234,56` and dates in `America/Sao_Paulo`. `GET /admin/notifications?tenantId=&status=` for PlatformAdmins.

## Notable Decisions
- Template rendering is a tiny replace + `HtmlEncoder` (no Razor/Scriban); a missing placeholder throws.
- Email links use `Notifications:PortalBaseUrl`; `Smtp:Enabled = false` records `Skipped`.
- Logs never contain recipients, tokens or message bodies.

## Test Plan
- Unit: all 9 templates render with no leftover placeholders, HTML encoding of malicious input, missing value throws, BRL formatting, São Paulo time, failure-reason translation.
- Integration (Mailpit via Testcontainers): invitation link + reprocessing = one email; password reset link without secrets; trial ending to admins only with the company name encoded; invoice issued/paid with BRL and local time; payment failed + suspension once; quota 80/100 to admins only; zero total sends nothing; admin log RBAC and filters; SMTP down → `Failed`, retried, gives up after 5.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
