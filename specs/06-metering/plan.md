# Plan — 06 Metering

## Branch and delivery

- **Branch:** `feat/metering-quota` (base: `develop`)
- **PR slicing:** 2 PRs: (1) módulo Projects no banco do tenant; (2) enforcement de quota + alertas + usage.

## Technical approach

`Subscribely.Modules.Projects` usa um `ProjectsDbContext` aberto pela `TenantDbContextFactory`
(conexão do tenant). Os endpoints de produto ficam num `RouteGroup` com o filtro
`QuotaEnforcementFilter` (do módulo Metering), que consulta os entitlements (cache) e executa o
script Lua. Alertas por `SET NX` + outbox. Snapshot por job.

## Affected files (by layer)

| Camada | Arquivo | Mudança |
|---|---|---|
| Produto | `src/Modules/Subscribely.Modules.Projects/Features/{List,Get,Create,Update,Delete}Project/*` | CRUD |
| Produto | `.../Persistence/{ProjectsDbContext.cs, Migrations/*}` | Migrations do banco do tenant |
| Metering | `src/Modules/Subscribely.Modules.Metering/Quota/{QuotaEnforcementFilter.cs, QuotaCounter.cs, quota.lua}` | Enforcement |
| Metering | `.../Features/{GetUsage, GetUsageHistory}/*`, `.../Jobs/UsageSnapshotJob.cs` | Consulta e histórico |
| Contracts | `.../Contracts/{QuotaThresholdReached.cs, IMeteringApi.cs}` | |

## Risks and mitigation

| Risco | Mitigação |
|---|---|
| Latência do Redis no caminho quente | Uma única ida ao Redis (Lua); multiplexer singleton |
| Migrations do banco do tenant divergentes | O provisionamento (spec 01) aplica o mesmo assembly de migrations; um job de startup aplica as pendentes em todos os tenants |

## Rollback and reversibility

- O filtro de quota pode ser desligado por configuração (`Metering:Enabled`).

## Testing strategy

- Unit: cálculo de `Retry-After`/reset, thresholds.
- Integration (Testcontainers Redis + Postgres): concorrência 200×100; 402 por status; alertas 1× por período; upgrade muda o limite; JWT negado em `/v1/projects`.
