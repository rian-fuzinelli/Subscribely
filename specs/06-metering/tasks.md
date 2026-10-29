# Tasks — 06 Metering

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `feat/metering-quota` (base: `develop`)

## Tasks

- [ ] T01 @database-engineer `ProjectsDbContext` no banco do tenant + aplicação de migrations em todos os tenants no startup — done when: tenant novo e existente ficam com a tabela `projects`
- [ ] T02 [P] @metering-engineer CRUD de Projects (somente API key) — done when: testes de integração verdes; JWT → 401 (deps: T01)
- [ ] T03 [P] @metering-engineer `QuotaCounter` com script Lua + cálculo de reset — done when: teste concorrente 200×100 exato
- [ ] T04 @metering-engineer `QuotaEnforcementFilter` (402 por status, 429, headers) — done when: testes de integração verdes (deps: T02, T03)
- [ ] T05 [P] @metering-engineer Alertas 80%/100% com `SET NX` + outbox — done when: 1 evento por threshold por período (deps: T04)
- [ ] T06 [P] @background-jobs-engineer `UsageSnapshotJob` + `GET /v1/usage` e `/v1/usage/history` — done when: o histórico reflete o contador (deps: T04)
- [ ] T07 @test-engineer Teste de fail-open com o Redis parado — done when: a requisição passa e o warning é logado (deps: T04)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [ ] Ampliar testes/CI — done when: pipeline verde
- [ ] Push da branch para a origem — done when: branch publicada
- [ ] PR draft (em inglês) e **pausa para aprovação** — done when: o humano aprovar

## Done
