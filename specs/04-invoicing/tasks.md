# Tasks — 04 Invoicing

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `feat/billing-invoicing` (base: `develop`)

## Tasks

- [ ] T01 [P] @billing-domain-engineer `ProrationCalculator` (função pura) — done when: testes de tabela e de propriedade verdes
- [ ] T02 [P] @database-engineer Tabelas `invoices`/`invoice_lines`, sequence de numeração, UNIQUE de período — done when: teste de duplicidade gera violação tratada
- [ ] T03 @billing-domain-engineer `Invoice` + `InvoiceFactory` (4 motivos, total 0 → Paid) — done when: testes unitários verdes (deps: T01, T02)
- [ ] T04 @billing-domain-engineer Ligar a factory ao `ChangePlan` e ao `PeriodEndJob` — done when: testes de integração de upgrade e renovação verdes (deps: T03)
- [ ] T05 [P] @billing-domain-engineer Endpoints List (cursor) / Get / Void — done when: testes de integração com RBAC verdes (deps: T03)
- [ ] T06 [P] @billing-domain-engineer `IBillingApi.MarkInvoicePaid` idempotente + `InvoicePaid` no outbox — done when: chamada dupla = 1 evento (deps: T03)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [ ] Ampliar testes/CI — done when: pipeline verde
- [ ] Push da branch para a origem — done when: branch publicada
- [ ] PR draft (em inglês) e **pausa para aprovação** — done when: o humano aprovar

## Done
