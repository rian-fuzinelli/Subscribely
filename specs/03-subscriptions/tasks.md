# Tasks — 03 Subscriptions

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `feat/billing-subscriptions` (base: `develop`)

## Tasks

- [ ] T01 [P] @billing-domain-engineer `BillingPeriod` (mensal/anual, âncora de fim de mês) — done when: testes de tabela verdes
- [ ] T02 [P] @billing-domain-engineer `PlanChangeClassifier` (upgrade/downgrade/igual/trial) — done when: testes unitários verdes
- [ ] T03 @billing-domain-engineer Agregado `Subscription` com todas as transições e eventos — done when: 100% de cobertura da máquina de estados (deps: T01, T02)
- [ ] T04 @database-engineer Configuração EF de `subscriptions` com `xmin` — done when: teste de concorrência devolve conflito (deps: T03)
- [ ] T05 @billing-domain-engineer Consumidor `OnTenantProvisioned` → trial — done when: signup termina com assinatura `Trialing` no teste (deps: T04)
- [ ] T06 [P] @billing-domain-engineer Endpoints Get/Cancel/Resume/CancelPendingChange — done when: testes de integração verdes (deps: T04)
- [ ] T07 [P] @billing-domain-engineer Endpoint ChangePlan (regras de seats via `ITenancyApi`) — done when: upgrade/downgrade/409 de seats verdes (deps: T04)
- [ ] T08 @background-jobs-engineer `TrialEndingNotifierJob`, `TrialExpiryJob`, `PeriodEndJob` (incluindo a migração para a versão vigente do plano) e `SuspensionExpiryJob` (30 dias → Canceled + Void), todos idempotentes — done when: testes com `FakeTimeProvider` verdes, execução dupla = no-op (deps: T05, T07)
- [ ] T09 @billing-domain-engineer Reactivate + consumidores `OnInvoicePaid`/`OnPaymentRetriesExhausted` — done when: testes de transição verdes (deps: T08)
- [ ] T10 @security-engineer Revisar a autorização dos endpoints e o 404 cruzado entre tenants — done when: suíte A×B cobre o módulo (deps: T06, T07)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [ ] Ampliar testes/CI — done when: pipeline verde
- [ ] Push da branch para a origem — done when: branch publicada
- [ ] PR draft (em inglês) e **pausa para aprovação** — done when: o humano aprovar

## Done
