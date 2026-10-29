# Plan — 01 Tenancy & Identity

## Branch and delivery

- **Branch:** `feat/tenancy-identity` (base: `develop`)
- **PR slicing:** 2 PRs: (1) signup + provisionamento + login/refresh; (2) convites/seats + API keys + isolamento.

## Technical approach

Módulo `Subscribely.Modules.Tenancy` com slices em `Features/` (Signup, ProvisionTenant, Login,
RefreshToken, Logout, InviteUser, AcceptInvitation, ApiKeys/*). `TenancyDbContext` no schema
`tenancy` do Catálogo. Provisionamento via job Hangfire usando `ConnectionStrings:PostgresAdmin`.
O handler de autenticação de API key resolve o tenant com cache Redis de 60 s e preenche
`ITenantContext`.

## Affected files (by layer)

| Camada | Arquivo | Mudança |
|---|---|---|
| Endpoints | `src/Modules/Subscribely.Modules.Tenancy/Features/<Slice>/<Slice>Endpoint.cs` | Um por slice |
| Domínio | `.../Domain/{Tenant.cs, User.cs, RefreshToken.cs, Invitation.cs, ApiKey.cs}` | Entidades com comportamento |
| Persistência | `.../Persistence/{TenancyDbContext.cs, Configurations/*.cs}` | Schema `tenancy` |
| Provisionamento | `.../Features/ProvisionTenant/{ProvisionTenantJob.cs, TenantDatabaseProvisioner.cs}` | CREATE DATABASE idempotente + migrations |
| Data plane | `src/BuildingBlocks/Subscribely.Infrastructure/Tenancy/TenantDbContextFactory.cs` | Abre o TenantDb pelo slug |
| Segurança | `.../Security/{JwtTokenService.cs, ApiKeyAuthenticationHandler.cs, Policies.cs}` | JWT, API key, políticas RBAC |
| Contracts | `.../Contracts/{ITenancyApi.cs, TenantProvisioned.cs}` | API pública do módulo |
| Testes | `tests/Subscribely.IntegrationTests/Tenancy/*`, `tests/Subscribely.UnitTests/Tenancy/*` | |

## Risks and mitigation

| Risco | Mitigação |
|---|---|
| Vazamento entre tenants | Toda query do Catálogo filtra por `ITenantContext.TenantId` (filtro global do EF); teste A×B obrigatório |
| Corrida no signup com o mesmo slug | Unique index + tradução de `23505` para 409 |
| Reuso de refresh token | Detecção por família + revogação total |

## Rollback and reversibility

- Migrations novas no schema `tenancy`; o `Down()` é testado (up → down → up).
- Bancos de tenant criados em teste são removidos pelo Testcontainers.

## Testing strategy

- Unit: entidades (convite expirado, último admin), geração e validação de JWT, formato de API key.
- Integration: signup → job → `Active`; login/refresh/reuso; seats; isolamento A×B; key revogada.
