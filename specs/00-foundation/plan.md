# Plan — 00 Foundation

> Só vale após a aprovação de `spec.md`. Aprovação → kickoff do `dev-lifecycle`.

## Branch and delivery

- **Branch:** `chore/foundation-modular-monolith` (base: `develop`)
- **PR slicing:** 2 PRs: (1) estrutura + build props + compose; (2) infraestrutura (DbContexts, tenancy base, Hangfire, outbox, erros, logs).

## Technical approach

Mover o projeto do template para `src/Subscribely.Api`, criar os BuildingBlocks e projetos de
módulo vazios (cada um com `Add<X>Module()`), centralizar pacotes e subir o compose. Depois,
implementar em `Subscribely.Infrastructure` as peças transversais (resolução de tenant, factory
de conexão por template, base de DbContext com schema por módulo, outbox, Hangfire) e os
middlewares do host.

## Affected files (by layer)

| Camada | Arquivo | Mudança |
|---|---|---|
| Solução | `Subscribely.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` | Novo layout e configuração central |
| Host | `src/Subscribely.Api/Program.cs` | Composição dos módulos, middlewares, health checks, OpenAPI/Scalar |
| Host | `src/Subscribely.Api/Middleware/CorrelationIdMiddleware.cs`, `TenantResolutionMiddleware.cs` | Correlação e preenchimento do `ITenantContext` |
| SharedKernel | `src/BuildingBlocks/Subscribely.SharedKernel/{Result.cs, Error.cs, Entity.cs, Money.cs, IDomainEvent.cs}` | Tipos base |
| Infra | `src/BuildingBlocks/Subscribely.Infrastructure/Tenancy/{ITenantContext.cs, TenantDatabaseFactory.cs}` | Contexto e factory de conexão |
| Infra | `.../Persistence/{ModuleDbContext.cs, CatalogConnection.cs}` | Base de DbContext com schema e interceptors |
| Infra | `.../Outbox/{OutboxMessage.cs, IOutbox.cs, OutboxProcessorJob.cs}` | Outbox transacional |
| Infra | `.../Jobs/HangfireSetup.cs` | Storage Postgres, dashboard autorizado |
| Módulos | `src/Modules/Subscribely.Modules.*/<X>Module.cs` | Projetos vazios com o método de registro |
| Testes | `tests/Subscribely.ArchitectureTests/ModuleBoundaryTests.cs` | Regra de fronteira |
| Infra Docker | `src/Subscribely.Api/Dockerfile`, `docker-compose.yml`, `.dockerignore`, `.env.example` | Ambiente local |

## Risks and mitigation

| Risco | Mitigação |
|---|---|
| Mover o projeto quebra o `Subscribely.Workspace` e a `.slnx` | Atualizar os caminhos na mesma tarefa; `dotnet build` verde como critério |
| `CREATE DATABASE` com slug injetado | Regex de slug + identificador entre aspas via `NpgsqlCommandBuilder.QuoteIdentifier` |
| Hangfire cria o schema na subida e disputa com migrations | `PrepareSchemaIfNecessary = true` só no schema `hangfire`, isolado dos módulos |

## Rollback and reversibility

- Sem dados de produção; reverter os PRs basta.

## Testing strategy

- Unit: `TenantDatabaseFactory` (template, slug inválido), `Money`, `Result`.
- Integration: health checks com Testcontainers; outbox grava na mesma transação e o job marca `processed_at`.
- Arquitetura: NetArchTest para as fronteiras.
