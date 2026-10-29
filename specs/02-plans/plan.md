# Plan — 02 Plans

## Branch and delivery

- **Branch:** `feat/billing-plans` (base: `develop`)
- **PR slicing:** PR único.

## Technical approach

Primeiro slice do módulo `Subscribely.Modules.Billing`: `BillingDbContext` (schema `billing`),
entidades `Plan`/`PlanVersion`, endpoints admin e público, seed de desenvolvimento e
`IBillingApi.GetEntitlements` com `HybridCache` (memória + Redis).

## Affected files (by layer)

| Camada | Arquivo | Mudança |
|---|---|---|
| Endpoints | `src/Modules/Subscribely.Modules.Billing/Features/Plans/{CreatePlan,UpdatePlan,ArchivePlan,ListPlans}/*Endpoint.cs` | Slices |
| Domínio | `.../Domain/Plans/{Plan.cs, PlanVersion.cs}` | Versionamento |
| Persistência | `.../Persistence/{BillingDbContext.cs, Configurations/PlanConfiguration.cs}` | Schema `billing` |
| Contracts | `.../Contracts/{IBillingApi.cs, Entitlements.cs}` | API pública |
| Seed | `.../Persistence/DevelopmentSeed.cs` | Planos e PlatformAdmin em Development |

## Risks and mitigation

| Risco | Mitigação |
|---|---|
| Cache de entitlements desatualizado | Invalidação por tag `tenant:{id}` quando a assinatura muda (spec 03) |

## Rollback and reversibility

- Migration aditiva; reverter o PR basta.

## Testing strategy

- Unit: validações de preço, criação de versão.
- Integration: CRUD admin com RBAC; listagem pública; 422 em plano arquivado.
