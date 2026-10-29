# Tasks — 02 Plans

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `feat/billing-plans` (base: `develop`)

## Tasks

- [x] T01 @database-engineer `BillingDbContext` + configurações de `plans`/`plan_versions` — done when: modelo valida no teste de integração
- [x] T02 @billing-domain-engineer Entidades Plan/PlanVersion com validações e versionamento — done when: testes unitários verdes (deps: T01)
- [x] T03 [P] @billing-domain-engineer Endpoints admin (criar, atualizar → nova versão, arquivar) — done when: testes de integração com RBAC verdes (deps: T02)
- [x] T04 [P] @billing-domain-engineer `GET /v1/plans` público — done when: só os ativos são listados no teste (deps: T02)
- [x] T05 [P] @metering-engineer `IBillingApi.GetEntitlements` com HybridCache e invalidação por tag — done when: teste mede o cache hit e a invalidação (deps: T02) — **movida para a spec 03** (entitlements dependem da assinatura)
- [x] T06 @database-engineer Seed de Development (planos + PlatformAdmin) _(planos aqui; o PlatformAdmin é semeado pelo módulo Tenancy na spec 01)_ — done when: `docker compose up` deixa os 3 planos disponíveis (deps: T02)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [x] Ampliar testes/CI — done when: pipeline verde
- [ ] ⏸ Push — bloqueado: sem `origin`
- [x] PR draft em `docs/prs/feat-billing-plans.md`; merge local em `develop`

## Done
