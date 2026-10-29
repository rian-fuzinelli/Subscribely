# Tasks — 00 Foundation

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `chore/foundation-modular-monolith` (base: `develop`)

## Tasks

- [x] T01 @orquestrador Commit inicial em `main`, criar `develop` — done when: `git branch` lista `main` e `develop`
- [x] T02 @software-architect Reorganizar em `src/`/`tests/`, mover a API para `src/Subscribely.Api`, remover o WeatherForecast, atualizar `.slnx` e Workspace — done when: `dotnet build` verde (deps: T01)
- [x] T03 @software-architect `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` — done when: build verde com `TreatWarningsAsErrors` (deps: T02)
- [x] T04 [P] @software-architect Criar SharedKernel (Result, Error, Entity, Money, IDomainEvent) com testes — done when: testes unitários verdes (deps: T03)
- [x] T05 [P] @software-architect Criar os 7 projetos de módulo vazios com `Add<X>Module()` e o teste de fronteira — done when: ArchitectureTests verdes (deps: T03)
- [ ] T06 [P] @devops-engineer Dockerfile multi-stage não-root, `docker-compose.yml` (api, postgres, redis, mailpit), `.env.example` — done when: `docker compose up` sobe e `/health/live` = 200 (deps: T03)
- [x] T07 [P] @database-engineer `ModuleDbContext` base (schema por módulo, convenção snake_case), conexão Catálogo — done when: teste de integração abre conexão no Testcontainers (deps: T04)
- [x] T08 [P] @security-engineer `ITenantContext` + `TenantDatabaseFactory` com validação de slug — done when: testes de template e de slug inválido verdes (deps: T04)
- [x] T09 @background-jobs-engineer Hangfire com storage Postgres + dashboard só para PlatformAdmin — done when: teste de integração enfileira e executa um job (deps: T07)
- [x] T10 @background-jobs-engineer Outbox transacional + `OutboxProcessorJob` recorrente com limite de 5 tentativas — done when: teste prova atomicidade e reprocessamento (deps: T07, T09)
- [ ] T11 [P] @software-architect ProblemDetails global, validação nativa, CorrelationId, logs JSON — done when: testes de 400/500 e header de correlação verdes (deps: T05)
- [ ] T12 @devops-engineer Health checks `live`/`ready` (Postgres, Redis) — done when: `ready` = 503 com Redis parado no teste (deps: T06, T07)
- [ ] T13 @software-architect OpenAPI + Scalar (Development) — done when: `/openapi/v1.json` = 200 (deps: T11)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [ ] @devops-engineer CI GitHub Actions (restore, build, testes com Testcontainers) — done when: pipeline verde
- [ ] Push da branch para a origem — done when: branch publicada
- [ ] PR draft (Summary / Notable Decisions / Test Plan, em inglês) e **pausa para aprovação** — done when: o humano aprovar

## Done
