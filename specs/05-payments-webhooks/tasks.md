# Tasks — 05 Payments, Gateway & Webhooks

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `feat/payments-webhooks` (base: `develop`)

## Tasks

- [x] T01 @database-engineer `PaymentsDbContext` + tabelas (payment_methods, charges, payment_events, dunning) — done when: modelo valida no teste
- [x] T02 [P] @security-engineer `WebhookSignatureVerifier` (HMAC, tempo constante, tolerância de 5 min) — done when: testes de válida/expirada/adulterada verdes
- [x] T03 [P] @payments-integration-engineer `IPaymentGateway` + `FakePaymentGateway` (tokens de teste, idempotência) + `HttpClient` com resiliência — done when: testes unitários do Fake verdes (deps: T01)
- [x] T04 @payments-integration-engineer `FakeWebhookSender` (job Hangfire, HTTP real assinado) — done when: teste de integração recebe o webhook (deps: T02, T03)
- [x] T05 @payments-integration-engineer Endpoint de recepção insert-first + `ProcessPaymentEventJob` (ordem, valor divergente) — done when: testes de duplicado/ordem/divergência verdes (deps: T04)
- [x] T06 [P] @payments-integration-engineer Meio de pagamento + `OnInvoiceIssued` → charge — done when: fluxo `pm_card_ok` termina com a fatura `Paid` (deps: T05)
- [x] T07 [P] @background-jobs-engineer `DunningJob` (D+1/D+3/D+7) + eventos `PaymentFailed`/`PaymentRetriesExhausted` — done when: `pm_card_declined` leva a `Suspended` no teste (deps: T05)
- [x] T08 @payments-integration-engineer `POST /v1/invoices/{id}/pay` + retentativa ao trocar o meio de pagamento — done when: `Suspended` → `Active` no teste (deps: T06, T07)
- [x] T09 @payments-integration-engineer Endpoint dev de replay — done when: replay devolve 200 e não altera o estado (deps: T05)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [x] Ampliar testes/CI — done when: pipeline verde
- [ ] ⏸ Push — bloqueado: sem `origin`
- [x] PR draft em `docs/prs/feat-payments-webhooks.md`; merge local em `develop`

## Done

- Desvios: o gateway Fake é servido pela própria API (`POST /fake-gateway/v1/charges`) e chamado por um `HttpClient` tipado com o resilience handler, para exercitar a idempotência e a resiliência de verdade; o replay também é mapeado no ambiente `Testing` (além de Development) para a suíte de integração; colunas extras `charges.tenant_id`, `charges.manual` (tentativas manuais não consomem a agenda do dunning), `payment_events.note` e `dunning.tenant_id/issued_at/resolved_at`.
