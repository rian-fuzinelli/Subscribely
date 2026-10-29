# Tasks — 09 E2E & Quality

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `test/e2e-scenarios` (base: `develop`)

## Tasks

- [ ] T01 @test-engineer Fixture (factory, containers, FakeTimeProvider, SyncJobClient, MailpitClient) — **antecipar para a onda 1** — done when: um teste de fumaça sobe a API contra os containers
- [ ] T02 @test-engineer DSL de cenário (Given/When/Then) — done when: o cenário 1 lê como prosa (deps: T01)
- [ ] T03 [P] @test-engineer Cenários 1 (feliz) e 2 (upgrade) — done when: verdes (deps: T02; specs 01–07 mergeadas)
- [ ] T04 [P] @test-engineer Cenários 3 (downgrade) e 4 (cancelamento) — done when: verdes (deps: T02)
- [ ] T05 [P] @test-engineer Cenários 5 (dunning) e 6 (idempotência) — done when: verdes (deps: T02)
- [ ] T06 [P] @test-engineer Cenários 7 (isolamento) e 8 (quota) — done when: verdes (deps: T02)
- [ ] T06b [P] @test-engineer Cenários 9 (suspensão longa + reativação) e 10 (recuperação de senha) — done when: verdes (deps: T02)
- [ ] T07 @devops-engineer CI final com cobertura (limites por módulo) + smoke do compose — done when: pipeline verde e o limite de cobertura é aplicado (deps: T03–T06b)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [ ] Push da branch para a origem — done when: branch publicada
- [ ] PR draft (em inglês) e **pausa para aprovação** — done when: o humano aprovar

## Done
