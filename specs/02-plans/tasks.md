# Tasks — 02 Plans

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `feat/billing-plans` (base: `develop`)

## Tasks

- [ ] T01 @database-engineer `BillingDbContext` + configurações de `plans`/`plan_versions` — done when: modelo valida no teste de integração
- [ ] T02 @billing-domain-engineer Entidades Plan/PlanVersion com validações e versionamento — done when: testes unitários verdes (deps: T01)
- [ ] T03 [P] @billing-domain-engineer Endpoints admin (criar, atualizar → nova versão, arquivar) — done when: testes de integração com RBAC verdes (deps: T02)
- [ ] T04 [P] @billing-domain-engineer `GET /v1/plans` público — done when: só os ativos são listados no teste (deps: T02)
- [ ] T05 [P] @metering-engineer `IBillingApi.GetEntitlements` com HybridCache e invalidação por tag — done when: teste mede o cache hit e a invalidação (deps: T02)
- [ ] T06 @database-engineer Seed de Development (planos + PlatformAdmin) — done when: `docker compose up` deixa os 3 planos disponíveis (deps: T02)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [ ] Ampliar testes/CI — done when: pipeline verde
- [ ] Push da branch para a origem — done when: branch publicada
- [ ] PR draft (em inglês) e **pausa para aprovação** — done when: o humano aprovar

## Done
