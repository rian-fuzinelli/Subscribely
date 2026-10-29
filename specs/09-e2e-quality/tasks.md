# Tasks — 09 E2E & Quality

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `test/e2e-scenarios` (base: `develop`)

## Tasks

- [x] T01 @test-engineer Fixture (factory, containers, FakeTimeProvider, SyncJobClient, MailpitClient) — **antecipar para a onda 1** — done when: um teste de fumaça sobe a API contra os containers
- [x] T02 @test-engineer DSL de cenário (Given/When/Then) — done when: o cenário 1 lê como prosa (deps: T01)
- [x] T03 [P] @test-engineer Cenários 1 (feliz) e 2 (upgrade) — done when: verdes (deps: T02; specs 01–07 mergeadas)
- [x] T04 [P] @test-engineer Cenários 3 (downgrade) e 4 (cancelamento) — done when: verdes (deps: T02)
- [x] T05 [P] @test-engineer Cenários 5 (dunning) e 6 (idempotência) — done when: verdes (deps: T02)
- [x] T06 [P] @test-engineer Cenários 7 (isolamento) e 8 (quota) — done when: verdes (deps: T02)
- [x] T06b [P] @test-engineer Cenários 9 (suspensão longa + reativação) e 10 (recuperação de senha) — done when: verdes (deps: T02)
- [x] T07 @devops-engineer CI final com cobertura (limites por módulo) + smoke do compose — done when: pipeline verde e o limite de cobertura é aplicado (deps: T03–T06b)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [ ] ⏸ Push — bloqueado: sem `origin`
- [x] PR draft em `docs/prs/test-e2e-scenarios.md`; merge local em `develop`

## Done

- Desvios: a cobertura usa `Microsoft.Testing.Extensions.CodeCoverage` (o `coverlet.collector` só funciona com VSTest e a suíte roda no Microsoft Testing Platform) + ReportGenerator + `scripts/check-coverage.py`; código gerado (logging, OpenAPI) e migrations ficam fora da medição. A CI não foi executada (sem remoto); o pipeline foi validado por partes localmente: testes com cobertura, relatório, gate e o smoke do compose (`/health/ready` em ~4 s) com um fluxo real de signup → produto. Resultado local: cobertura total 93%, Billing 93,1%, Payments 96,2%, classes críticas 100%. O cenário 6 lê o id do evento do gateway no banco (não há API que o exponha).
