# Spec — 02 Plans (catálogo de planos)

> Status: **APROVADO** — execução autônoma autorizada pelo humano em 2026-10-28. Herda as decisões de `specs/OVERVIEW.md`.

## Context and problem

O PlatformAdmin precisa definir os planos comercializados, com preço mensal e anual, quota
mensal de requisições e número máximo de assentos. Assinaturas, quota e seats leem esses limites,
então as mudanças em um plano não podem quebrar quem já o assina.

## Acceptance criteria (verifiable)

- [ ] `POST /admin/plans` cria um plano; `code` é único e imutável.
- [ ] `PUT /admin/plans/{code}` altera nome, preços e limites e cria uma **nova versão** do plano; as assinaturas existentes continuam na versão que contrataram até a próxima renovação.
- [ ] `POST /admin/plans/{code}/archive` impede novas assinaturas; as assinaturas existentes não são afetadas.
- [ ] `GET /v1/plans` (anônimo) lista só os planos ativos, com a versão vigente.
- [ ] `IBillingApi.GetEntitlements(tenantId)` devolve `{ requestQuotaMonthly, maxSeats }` da assinatura vigente em < 5 ms com cache (invalidado quando a assinatura muda).
- [ ] Um seed de desenvolvimento cria os planos `starter`, `pro` e `business` e um usuário PlatformAdmin.

## Out of scope (explicit)

- Cupons, descontos, impostos, multi-moeda, add-ons, preço por seat.

## Data contracts

- **`POST /admin/plans`** in: `{ code: ^[a-z0-9-]{2,30}$, name: string(1..60), monthlyPriceCents: long>=0, annualPriceCents: long>=0, requestQuotaMonthly: int>0, maxSeats: int>0 }` → 201 com o plano.
- **Catálogo `billing`:**
  - `plans(id uuid PK, code citext UNIQUE, status[Active|Archived], created_at)`
  - `plan_versions(id uuid PK, plan_id FK, version int, name, monthly_price_cents bigint, annual_price_cents bigint, request_quota_monthly int, max_seats int, created_at, UNIQUE(plan_id, version))`
- **Contracts:** `IBillingApi.GetEntitlements(tenantId)`, `IBillingApi.GetActivePlanVersion(code)`.

## Authorization

- `/admin/plans*`: PlatformAdmin. `GET /v1/plans`: anônimo.

## Edge cases and failure modes

| Caso | Comportamento esperado |
|---|---|
| `code` duplicado | 409 |
| Preço anual maior que 12× o mensal | 422 `annual-price-invalid` |
| Arquivar um plano com assinantes | Permitido; afeta só as novas assinaturas |
| Signup com um plano arquivado | 422 `plan-unavailable` |
| Reduzir `maxSeats` numa nova versão | Só vale para os assinantes na renovação (ver spec 03, downgrade com excesso de seats) |

## Decisions taken

| Decisão | Alternativa rejeitada | Por quê |
|---|---|---|
| Versionamento imutável (`plan_versions`) | Editar o plano no lugar | A fatura e a assinatura apontam para os limites e preços exatos contratados |
| Preço free (0) permitido | Proibir | Permite um plano gratuito sem caso especial |
| Migrar para a versão vigente na renovação | Grandfathering permanente | Evita acumular versões antigas ativas; o período atual mantém o preço contratado |

## Constraints

- Leitura de entitlements no caminho quente da quota: cache em memória/Redis.

## Open questions (risks)

- Nenhuma. Resolvida: quem já assina migra para a versão vigente na próxima renovação (sem grandfathering). Se o plano tiver sido arquivado, a renovação mantém a última versão.
