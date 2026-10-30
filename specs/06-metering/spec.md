# Spec — 06 Metering (quota de requisições e produto demo)

> Status: **APROVADO** — execução autônoma autorizada pelo humano em 2026-10-28. Herda as decisões de `specs/OVERVIEW.md`.

## Context and problem

Os planos limitam as requisições mensais aos endpoints de produto. O enforcement precisa ser
atômico sob concorrência, barato (está no caminho de toda requisição de produto) e alertar o
tenant antes de bloquear. Para haver o que medir, existe um produto demo, `Projects`, que vive no
banco do tenant (data plane).

## Acceptance criteria (verifiable)

- [ ] Produto demo `/v1/projects` (CRUD: `GET` lista, `GET {id}`, `POST`, `PUT {id}`, `DELETE {id}`) autenticado **somente** por API key, persistido no banco do tenant.
- [ ] Ordem do pipeline nos endpoints de produto: autenticar a API key → checar o status da assinatura → checar e incrementar a quota → executar.
- [ ] Assinatura `Expired`, `Suspended` ou `Canceled` → 402 `subscription-inactive` (não consome quota).
- [ ] A quota usa um script Lua atômico no Redis: chave `quota:{tenantId}:{yyyyMM}` (UTC), `INCR` só se `atual < limite`, `EXPIRE` de 35 dias na criação.
- [ ] Acima do limite → 429 `quota-exceeded` com `Retry-After` (segundos até 00:00 UTC do dia 1 do próximo mês); a requisição rejeitada não incrementa.
- [ ] Toda resposta de produto inclui `X-RateLimit-Limit`, `X-RateLimit-Remaining` e `X-RateLimit-Reset` (epoch).
- [ ] Toda requisição autenticada que passa do enforcement conta, independente do status HTTP do handler.
- [ ] Ao cruzar 80% e 100% do limite, publica `QuotaThresholdReached { tenantId, period, threshold }` **uma única vez** por período (`SET NX` em `quota-alert:{tenantId}:{period}:{threshold}`).
- [ ] Upgrade no meio do mês → o novo limite vale na próxima requisição (cache de entitlements invalidado); o contador é preservado.
- [ ] `GET /v1/usage` → `{ period, requestsUsed, requestLimit, seatsUsed, seatLimit }` (TenantAdmin e Member).
- [ ] `UsageSnapshotJob` (de hora em hora) persiste o contador em `metering.usage_periods` para histórico; `GET /v1/usage/history` devolve os últimos 12 meses.
- [ ] Teste de carga em integração: 200 requisições concorrentes com limite 100 → exatamente 100 sucessos e 100 respostas 429.

## Out of scope (explicit)

- Rate limit por segundo (burst), métricas de uso além de requisições e seats, cobrança por excedente (overage).

## Data contracts

- **Tenant DB:** `projects(id uuid PK, name varchar(100), description text NULL, created_at, updated_at)`
- **Catálogo `metering`:** `usage_periods(tenant_id, period char(7) 'yyyy-MM', requests_count bigint, updated_at, PK(tenant_id, period))`
- **Evento:** `QuotaThresholdReached { tenantId, period, threshold: 80|100, limit }`
- **Contracts consumidos:** `IBillingApi.GetEntitlements(tenantId)` (inclui o status da assinatura), `ITenancyApi.CountActiveSeats`.

## Authorization

- `/v1/projects`: API key **ou** a sessão (JWT) de um TenantAdmin — emenda da spec 10 (playground do portal), com as mesmas regras de status, quota, alertas e auditoria; Member → 403; sem credencial → 401. `/v1/usage*`: TenantAdmin e Member.

## Edge cases and failure modes

| Caso | Comportamento esperado |
|---|---|
| Redis indisponível | **Fail-open** (requisição liberada), log de warning e métrica `quota_check_failed`; o uso durante a queda não é contado |
| Virada do mês durante as requisições | Nova chave; o mês anterior expira sozinho |
| Downgrade aplicado com o uso acima do novo limite | Bloqueia até o próximo mês (429) |
| Projeto de outro tenant | Impossível por construção (banco separado); `GET {id}` inexistente → 404 |

## Decisions taken

| Decisão | Alternativa rejeitada | Por quê |
|---|---|---|
| Janela mensal de calendário UTC | Âncora no dia da assinatura | KISS; igual para planos anuais |
| Lua check-and-incr | `INCR` + `DECR` em caso de excesso | Sem janela de corrida nem contagem inflada |
| Fail-open com Redis fora | Fail-closed (503) | A queda do Redis não derruba o produto de todos os tenants |
| Snapshot por hora no Postgres | Gravar no Postgres por requisição | Redis absorve o caminho quente |

## Constraints

- Overhead do enforcement: p95 < 5 ms (Redis local).

## Open questions (risks)

- Nenhuma. Resolvida: **fail-open** quando o Redis cai (disponibilidade do produto acima do enforcement estrito).
