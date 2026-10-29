# Tasks — 04 Invoicing

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `feat/billing-invoicing` (base: `develop`)

## Tasks

- [x] T01 [P] @billing-domain-engineer `ProrationCalculator` (função pura) — done when: testes de tabela e de propriedade verdes
- [x] T02 [P] @database-engineer Tabelas `invoices`/`invoice_lines`, sequence de numeração, UNIQUE de período — done when: teste de duplicidade gera violação tratada
- [x] T03 @billing-domain-engineer `Invoice` + `InvoiceFactory` (4 motivos, total 0 → Paid) — done when: testes unitários verdes (deps: T01, T02)
- [x] T04 @billing-domain-engineer Ligar a factory ao `ChangePlan` e ao `PeriodEndJob` — done when: testes de integração de upgrade e renovação verdes (deps: T03)
- [x] T05 [P] @billing-domain-engineer Endpoints List (cursor) / Get / Void — done when: testes de integração com RBAC verdes (deps: T03)
- [x] T06 [P] @billing-domain-engineer `IBillingApi.MarkInvoicePaid` idempotente + `InvoicePaid` no outbox — done when: chamada dupla = 1 evento (deps: T03)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [x] Ampliar testes/CI — done when: pipeline verde
- [ ] ⏸ Push — bloqueado: sem `origin`
- [x] PR draft em `docs/prs/feat-billing-invoicing.md`; merge local em `develop`

## Done

- Desvios: a fatura de upgrade usa o período `[agora, fim)` (o restante prorrateado), o que mantém o UNIQUE `(subscription_id, period_start, reason)` compatível com dois upgrades no mesmo período; uma soma negativa ganha uma linha de ajuste para o total ficar 0 sem crédito transportado; `IBillingApi.ListOpenInvoicesAsync` adicionado para a spec 05.
