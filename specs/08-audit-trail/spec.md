# Spec — 08 Audit Trail (trilha de auditoria)

> Status: **APROVADO** — execução autônoma autorizada pelo humano em 2026-10-28. Herda as decisões de `specs/OVERVIEW.md`.

## Context and problem

Billing e segurança exigem saber **quem fez o quê, quando e de onde**, inclusive ações de jobs e
webhooks. A trilha precisa ser gravada atomicamente com a mudança, não pode ser alterada e precisa
respeitar a divisão entre control plane e data plane.

## Acceptance criteria (verifiable)

- [ ] Mudanças de entidades marcadas `[Auditable]` geram uma entrada na **mesma transação** do `SaveChanges` (interceptor do EF), com `before`/`after` em JSON.
- [ ] Campos marcados `[NotAudited]` (ex.: `password_hash`, `key_hash`, `token_hash`, `gateway_token`) nunca aparecem no JSON.
- [ ] Ações explícitas também são auditadas: login com sucesso/falha, logout, reuso de refresh token, API key criada/revogada, webhook recebido/rejeitado, void de fatura, acesso ao dashboard do Hangfire.
- [ ] Ator identificado: `User(id)`, `ApiKey(id)`, `System(jobName)` ou `Webhook(provider)`, com `correlation_id` e IP (quando houver request).
- [ ] **Control plane** → `audit.entries` no Catálogo (com `tenant_id`, nulo em ações de plataforma). **Data plane** (projetos) → `audit_entries` no banco do tenant.
- [ ] Append-only: o usuário `app` não tem `UPDATE`/`DELETE` nas tabelas de auditoria (GRANT) **e** um trigger rejeita `UPDATE`/`DELETE`.
- [ ] `GET /v1/audit?source=control|product&from&to&action&cursor` (TenantAdmin, só o próprio tenant, paginado por cursor, 50 por página).
- [ ] `GET /admin/audit?tenantId&from&to&action&cursor` (PlatformAdmin, control plane).

## Out of scope (explicit)

- Exportação para SIEM, assinatura criptográfica/encadeamento de hashes, UI.
- Retenção e expurgo: na v1, a trilha é mantida indefinidamente (política de LGPD vira spec futura).

## Data contracts

- **`audit.entries` (Catálogo) e `audit_entries` (tenant DB):** `id uuid, tenant_id uuid NULL, occurred_at timestamptz, actor_type[User|ApiKey|System|Webhook], actor_id text, action text (ex.: subscription.plan_changed), entity_type text NULL, entity_id text NULL, before jsonb NULL, after jsonb NULL, correlation_id text, ip inet NULL`
- Índices: `(tenant_id, occurred_at DESC)`, `(action, occurred_at DESC)`.
- **Contracts:** `IAuditLog.Record(action, entityType?, entityId?, data?)` para ações explícitas.

## Authorization

- TenantAdmin vê só o próprio tenant; Member não vê; PlatformAdmin vê o control plane de todos.

## Edge cases and failure modes

| Caso | Comportamento esperado |
|---|---|
| Falha ao gravar a auditoria | A transação inteira faz rollback (nada muda sem trilha) |
| Login com e-mail inexistente | Auditado com `actor_type=User`, `actor_id=anonymous` e o e-mail com hash (sem o e-mail em claro) |
| Tentativa de `UPDATE` via SQL | Erro do trigger |

## Decisions taken

| Decisão | Alternativa rejeitada | Por quê |
|---|---|---|
| Interceptor do EF na mesma transação | Auditoria via outbox assíncrono | Garantia de atomicidade |
| GRANT + trigger | Só a convenção | Defesa em profundidade |
| Duas trilhas (control/data) | Uma trilha central para os dados de produto | Respeita o isolamento por banco do tenant |

## Constraints

- LGPD: e-mails e IPs são dados pessoais. Retenção indefinida na v1 é um risco aceito, a tratar numa spec futura de retenção.

## Open questions (risks)

- Nenhuma. Resolvida: retenção indefinida na v1 (sem job de expurgo).
