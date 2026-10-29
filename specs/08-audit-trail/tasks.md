# Tasks — 08 Audit Trail

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `feat/audit-trail` (base: `develop`)

## Tasks

- [x] T01 @database-engineer Tabela `audit.entries` + `audit_entries` no tenant, índices, GRANT e trigger append-only — done when: o teste de `UPDATE` falha com o erro do trigger
- [x] T02 [P] @software-architect Atributos + `AuditSaveChangesInterceptor` + `AuditActorAccessor` — done when: testes unitários verdes (deps: T01)
- [x] T03 [P] @security-engineer Teste de reflexão que exige `[NotAudited]` em campos sensíveis — done when: o teste falha ao remover o atributo de `password_hash`
- [x] T04 @software-architect `IAuditLog` para ações explícitas (`Record` transacional + `WriteAsync` imediato) _(as chamadas em login/API key/webhook/void entram nas specs 01, 04 e 05, junto das features)_ + chamadas em login/API key/webhook/void/dashboard — done when: os testes de integração encontram as entradas (deps: T02)
- [x] T05 [P] @software-architect Endpoints de consulta (tenant e admin) com cursor — done when: testes de isolamento e paginação verdes (deps: T02)
- [x] T06 @test-engineer Teste de atomicidade (falha na auditoria → rollback) — done when: verde (deps: T02)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [x] Ampliar testes/CI — done when: pipeline verde
- [ ] ⏸ Push — bloqueado: sem `origin`
- [x] PR draft em `docs/prs/feat-audit-trail.md`; merge local em `develop`

> Nota: `GET /v1/audit?source=product` responde 400 `audit-source-unavailable` até o módulo Projects (spec 06) implementar `IProductAuditSource`.

## Done
