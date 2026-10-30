# Tasks — 06 Metering

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída.

**Branch:** `feat/metering-quota` (base: `develop`)

## Tasks

- [x] T01 @database-engineer `ProjectsDbContext` no banco do tenant + aplicação de migrations em todos os tenants no startup — done when: tenant novo e existente ficam com a tabela `projects`
- [x] T02 [P] @metering-engineer CRUD de Projects (somente API key) — done when: testes de integração verdes; JWT → 401 (deps: T01)
- [x] T03 [P] @metering-engineer `QuotaCounter` com script Lua + cálculo de reset — done when: teste concorrente 200×100 exato
- [x] T04 @metering-engineer `QuotaEnforcementFilter` (402 por status, 429, headers) — done when: testes de integração verdes (deps: T02, T03)
- [x] T05 [P] @metering-engineer Alertas 80%/100% com `SET NX` + outbox — done when: 1 evento por threshold por período (deps: T04)
- [x] T06 [P] @background-jobs-engineer `UsageSnapshotJob` + `GET /v1/usage` e `/v1/usage/history` — done when: o histórico reflete o contador (deps: T04)
- [x] T07 @test-engineer Teste de fail-open com o Redis parado — done when: a requisição passa e o warning é logado (deps: T04)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [x] Ampliar testes/CI — done when: pipeline verde
- [ ] ⏸ Push — bloqueado: sem `origin`
- [x] PR draft em `docs/prs/feat-metering-quota.md`; merge local em `develop`

## Done

- Desvios: o gate de produto é a interface `IProductRequestGate` na Infrastructure (implementada pelo Metering), para o Projects não depender de outro módulo; os assentos de `/v1/usage` são os ocupados (ativos + convites pendentes), como na política de seats; o teste de fail-open aponta o Redis para uma porta fechada em vez de parar o container compartilhado; `ITenancyApi.ListActiveTenantSlugsAsync` para migrar os bancos existentes na subida.
