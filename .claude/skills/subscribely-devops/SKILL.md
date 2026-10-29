---
name: subscribely-devops
description: DevOps do Subscribely — Dockerfile multi-stage não-root para .NET 10, docker-compose (api, postgres, redis, mailpit) com health checks e .env, configuração por variáveis de ambiente, health endpoints live/ready e pipeline GitHub Actions com Testcontainers e cobertura. Use em containers, compose, CI/CD, configuração de ambiente e health checks.
---

# DevOps — Subscribely

## Dockerfile (`src/Subscribely.Api/Dockerfile`)
- Multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` (restore com cache de camadas: copie primeiro `*.slnx`, `Directory.*.props` e os `*.csproj`) → `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime).
- `USER $APP_UID` (não-root), `ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080`.
- `.dockerignore` exclui `bin/`, `obj/`, `.git/`, `.vs/`, `.idea/`, `.claude/`, `specs/`, `tests/`.

## docker-compose.yml (raiz)
| Serviço | Imagem | Portas | Health check |
|---|---|---|---|
| api | build local | 8080 | `curl -f http://localhost:8080/health/live` |
| postgres | `postgres:17` | 5432 | `pg_isready` |
| redis | `redis:8` | 6379 | `redis-cli ping` |
| mailpit | `axllent/mailpit` (tag fixa) | 1025 (SMTP), 8025 (UI) | — |
- `api` com `depends_on: condition: service_healthy` para o postgres e o redis.
- Volume nomeado para o Postgres; script `docker/postgres/init.sql` cria os usuários `app` (sem CREATEDB) e `provisioner` (CREATEDB).
- Segredos via `.env` (gitignored) + `.env.example` versionado com placeholders.

## Configuração
- Hierarquia: `appsettings.json` (sem segredos) → `appsettings.{Env}.json` → user-secrets (dev) → variáveis de ambiente (`ConnectionStrings__Catalog`, `Jwt__SigningKey`...).
- Options pattern com `ValidateDataAnnotations().ValidateOnStart()`: a API não sobe com configuração inválida.

## Health checks
- `/health/live`: processo de pé (sem dependências).
- `/health/ready`: Postgres (Catálogo) + Redis. O SMTP **não** entra (não é crítico).

## CI (`.github/workflows/ci.yml`)
- Gatilhos: `pull_request` e `push` em `develop`/`main`.
- Passos: checkout → `actions/setup-dotnet` (10.0.x) → cache do NuGet → `dotnet restore` → `dotnet build --no-restore -c Release` → testes unit + arch + integration (Docker disponível no `ubuntu-latest`) → cobertura (Coverlet + ReportGenerator) com limite por módulo → smoke `docker compose up -d` + espera do `/health/ready`.

## Skills relacionadas
`dotnet-claude-kit:docker`, `dotnet-claude-kit:ci-cd`, `dotnet-claude-kit:configuration`, `dotnet-claude-kit:container-publish`.
