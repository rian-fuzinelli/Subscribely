---
name: subscribely-metering
description: Medição de uso do Subscribely — quota mensal de requisições em Redis com script Lua atômico, filtro de enforcement (402/429 e headers X-RateLimit), alertas de 80%/100% deduplicados, cache de entitlements com HybridCache e o produto demo Projects no banco do tenant. Use em quota, limites, Redis, cache e endpoints de produto.
---

# Metering e quota — Subscribely

## Pipeline dos endpoints de produto (`/v1/projects`)
`ApiKey auth` → `status da assinatura` (Expired/Suspended/Canceled → **402**) → `quota` (excedeu → **429**) → handler.

## Contador (Redis)
- Chave `quota:{tenantId}:{yyyyMM}` (mês de calendário UTC). EXPIRE de 35 dias na criação.
- Script Lua **atômico** (uma ida ao Redis):
```lua
local current = tonumber(redis.call('GET', KEYS[1]) or '0')
if current >= tonumber(ARGV[1]) then return -1 end
local n = redis.call('INCR', KEYS[1])
if n == 1 then redis.call('EXPIRE', KEYS[1], ARGV[2]) end
return n
```
- Requisição rejeitada **não** incrementa. Requisição aceita conta mesmo se o handler devolver 4xx/5xx.
- Headers em toda resposta de produto: `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset` (epoch do 1º dia do próximo mês, 00:00 UTC); 429 também traz `Retry-After` (segundos).

## Alertas
- Ao cruzar `ceil(0.8 × limite)` e `limite`: `SET quota-alert:{tenantId}:{period}:{80|100} 1 NX EX 3024000`; só quem ganhou o `NX` publica `QuotaThresholdReached` no outbox.

## Entitlements
- `IBillingApi.GetEntitlements(tenantId)` → `{ status, requestQuotaMonthly, maxSeats }` via `HybridCache` (memória + Redis), tag `tenant:{id}`, TTL de 5 min; o Billing invalida a tag em toda mudança de assinatura.

## Falhas
- Redis indisponível → **fail-open** (decisão aprovada) + `LogWarning` + contador de métrica. Nunca derrubar o produto por causa da quota.

## Produto demo
- `ProjectsDbContext` no banco do tenant via `TenantDbContextFactory`. Sem `TenantId` nas tabelas: o isolamento é físico.

## Testes exigidos
- Concorrência: 200 requisições paralelas com limite 100 → exatamente 100 × 2xx e 100 × 429.
- Alerta disparado uma única vez por threshold por período.

## Skills relacionadas
`dotnet-claude-kit:caching`, `backend-design:performance-and-scaling`.
