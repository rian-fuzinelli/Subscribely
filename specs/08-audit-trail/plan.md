# Plan — 08 Audit Trail

## Branch and delivery

- **Branch:** `feat/audit-trail` (base: `develop`)
- **PR slicing:** PR único.

## Technical approach

`AuditSaveChangesInterceptor` em `Subscribely.Infrastructure`, registrado em todo `ModuleDbContext`
e no `ProjectsDbContext`. A entidade `AuditEntry` é mapeada nos dois bancos; as migrations e o SQL
de GRANT/trigger pertencem ao módulo Audit (Catálogo) e ao assembly de migrations do tenant. O
ator vem de `ITenantContext` + `HttpContext`/contexto do job.

## Affected files (by layer)

| Camada | Arquivo | Mudança |
|---|---|---|
| Infra | `src/BuildingBlocks/Subscribely.Infrastructure/Auditing/{AuditableAttribute.cs, NotAuditedAttribute.cs, AuditSaveChangesInterceptor.cs, AuditActorAccessor.cs}` | Captura |
| Módulo | `src/Modules/Subscribely.Modules.Audit/{Persistence/AuditDbContext.cs, Features/QueryAudit/*, Features/AdminQueryAudit/*, Contracts/IAuditLog.cs}` | Consulta e API |
| SQL | Migrations com `REVOKE UPDATE, DELETE` + trigger `audit_append_only` | Imutabilidade |

## Risks and mitigation

| Risco | Mitigação |
|---|---|
| Vazamento de segredos no JSON | Teste de reflexão: toda propriedade com `hash`/`token`/`secret` no nome precisa de `[NotAudited]` |
| Volume da tabela | Índices + paginação por cursor; retenção indefinida na v1 (volume de demo) |

## Rollback and reversibility

- Migration aditiva. O interceptor pode ser desligado por flag em caso de emergência (com aprovação explícita).

## Testing strategy

- Unit: serialização com exclusão de campos; resolução do ator.
- Integration: mudança + auditoria atômicas (falha na auditoria → rollback); trigger bloqueia o `UPDATE`; isolamento da consulta entre tenants.
