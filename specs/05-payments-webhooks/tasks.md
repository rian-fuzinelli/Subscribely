# Tasks — 05 Payments, Gateway & Webhooks

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `feat/payments-webhooks` (base: `develop`)

## Tasks

- [ ] T01 @database-engineer `PaymentsDbContext` + tabelas (payment_methods, charges, payment_events, dunning) — done when: modelo valida no teste
- [ ] T02 [P] @security-engineer `WebhookSignatureVerifier` (HMAC, tempo constante, tolerância de 5 min) — done when: testes de válida/expirada/adulterada verdes
- [ ] T03 [P] @payments-integration-engineer `IPaymentGateway` + `FakePaymentGateway` (tokens de teste, idempotência) + `HttpClient` com resiliência — done when: testes unitários do Fake verdes (deps: T01)
- [ ] T04 @payments-integration-engineer `FakeWebhookSender` (job Hangfire, HTTP real assinado) — done when: teste de integração recebe o webhook (deps: T02, T03)
- [ ] T05 @payments-integration-engineer Endpoint de recepção insert-first + `ProcessPaymentEventJob` (ordem, valor divergente) — done when: testes de duplicado/ordem/divergência verdes (deps: T04)
- [ ] T06 [P] @payments-integration-engineer Meio de pagamento + `OnInvoiceIssued` → charge — done when: fluxo `pm_card_ok` termina com a fatura `Paid` (deps: T05)
- [ ] T07 [P] @background-jobs-engineer `DunningJob` (D+1/D+3/D+7) + eventos `PaymentFailed`/`PaymentRetriesExhausted` — done when: `pm_card_declined` leva a `Suspended` no teste (deps: T05)
- [ ] T08 @payments-integration-engineer `POST /v1/invoices/{id}/pay` + retentativa ao trocar o meio de pagamento — done when: `Suspended` → `Active` no teste (deps: T06, T07)
- [ ] T09 @payments-integration-engineer Endpoint dev de replay — done when: replay devolve 200 e não altera o estado (deps: T05)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [ ] Ampliar testes/CI — done when: pipeline verde
- [ ] Push da branch para a origem — done when: branch publicada
- [ ] PR draft (em inglês) e **pausa para aprovação** — done when: o humano aprovar

## Done
