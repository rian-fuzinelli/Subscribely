---
name: subscribely-database
description: Banco de dados do Subscribely — PostgreSQL com EF Core 10, banco Catálogo com schema por módulo, um banco por tenant via template de conexão, provisionamento idempotente, constraints, índices, concorrência otimista e auditoria append-only. Use em qualquer tarefa que crie ou altere entidades, DbContexts, queries ou SQL.
---

# Banco de dados — Subscribely

## Topologia
- **Catálogo**: `ConnectionStrings:Catalog`. Schemas: `tenancy`, `billing`, `payments`, `metering`, `notifications`, `audit`, `outbox`, `hangfire`.
- **Tenant**: `TenantDatabase:ConnectionTemplate` com `{slug}` → `subscribely_t_{slug}`. Abra sempre via `TenantDbContextFactory`; nunca monte a connection string manualmente.
- **Admin** (`ConnectionStrings:PostgresAdmin`): só o provisionador usa (`CREATE DATABASE`). O usuário `app` não tem `CREATEDB` nem superuser.

## Modelagem (Npgsql + EF Core 10)
- Nomes `snake_case` (convenção global), PK `uuid` gerada na aplicação (`Guid.CreateVersion7()`).
- Dinheiro `bigint` (`*_cents`); datas `timestamptz` em UTC; enums como `text` com `HasConversion<string>()`.
- `citext` para e-mail/slug/code. JSON em `jsonb`.
- Toda regra de unicidade **vira índice UNIQUE** (nunca só uma checagem na aplicação). Traduza `23505` (`PostgresErrorCodes.UniqueViolation`) para 409/no-op.
- Toda FK tem índice. Queries de lista: paginação por cursor (`(occurred_at, id) < (@t, @id)`), nunca `OFFSET` alto.
- Concorrência otimista em agregados disputados (assinatura): `xmin` como token (`UseXminAsConcurrencyToken`). Trate `DbUpdateConcurrencyException`.
- Configuração por `IEntityTypeConfiguration<T>` dentro do módulo; `ModuleDbContext` define o schema.

## Migrations
- **Implementers não geram migrations.** Descreva no relatório a mudança de modelo; o orquestrador roda `dotnet ef migrations add` após o merge.
- Toda migration precisa de `Down()` funcional; teste up → down → up.
- SQL manual (GRANT, trigger, sequence) via `migrationBuilder.Sql(...)`, com o par reverso no `Down`.

## Provisionamento de tenant (idempotente)
1. Validar o slug `^[a-z0-9-]{3,40}$`.
2. `SELECT 1 FROM pg_database WHERE datname = @name` → se não existir, `CREATE DATABASE` com `NpgsqlCommandBuilder.QuoteIdentifier` (sem transação, porque o Postgres não permite).
3. Aplicar as migrations do assembly do tenant (`Database.MigrateAsync`).
4. Marcar `Active`. Cada passo pode ser reexecutado sem efeito colateral.

## Auditoria append-only
- `REVOKE UPDATE, DELETE ON audit.entries FROM app;` + trigger `BEFORE UPDATE OR DELETE ... RAISE EXCEPTION`.

## Anti-padrões proibidos
- N+1 (use projeção `Select` para DTO, `AsNoTracking` em leitura).
- `IgnoreQueryFilters()` sem aprovação explícita na spec.
- SQL interpolado com dado do usuário (use parâmetros / `FromSql` com interpolação segura).

## Skills relacionadas
`dotnet-claude-kit:ef-core`, `backend-design:data-modeling-discipline`, `backend-design:migration-safety`, `backend-design:query-discipline`.
