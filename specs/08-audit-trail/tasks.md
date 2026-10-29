# Tasks — 08 Audit Trail

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `feat/audit-trail` (base: `develop`)

## Tasks

- [ ] T01 @database-engineer Tabela `audit.entries` + `audit_entries` no tenant, índices, GRANT e trigger append-only — done when: o teste de `UPDATE` falha com o erro do trigger
- [ ] T02 [P] @software-architect Atributos + `AuditSaveChangesInterceptor` + `AuditActorAccessor` — done when: testes unitários verdes (deps: T01)
- [ ] T03 [P] @security-engineer Teste de reflexão que exige `[NotAudited]` em campos sensíveis — done when: o teste falha ao remover o atributo de `password_hash`
- [ ] T04 @software-architect `IAuditLog` para ações explícitas + chamadas em login/API key/webhook/void/dashboard — done when: os testes de integração encontram as entradas (deps: T02)
- [ ] T05 [P] @software-architect Endpoints de consulta (tenant e admin) com cursor — done when: testes de isolamento e paginação verdes (deps: T02)
- [ ] T06 @test-engineer Teste de atomicidade (falha na auditoria → rollback) — done when: verde (deps: T02)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [ ] Ampliar testes/CI — done when: pipeline verde
- [ ] Push da branch para a origem — done when: branch publicada
- [ ] PR draft (em inglês) e **pausa para aprovação** — done when: o humano aprovar

## Done
