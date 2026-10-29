# Spec — 00 Foundation (arquitetura, infraestrutura e multi-tenancy base)

> Status: **APROVADO** — execução autônoma autorizada pelo humano em 2026-10-28. Herda as decisões de `specs/OVERVIEW.md`.

## Context and problem

Todos os módulos dependem de uma base comum: a estrutura de Modular Monolith, o acesso ao banco
Catálogo e aos bancos por tenant, Redis, Hangfire, outbox, tratamento de erros e o ambiente Docker.
Sem essa fundação, os implementers paralelos divergiriam em padrões e gerariam conflitos.

## Acceptance criteria (verifiable)

- [ ] Solução reorganizada no layout de `OVERVIEW.md`; o template WeatherForecast foi removido.
- [ ] `Directory.Build.props` (net10.0, nullable, warnings como erro, analyzers) e `Directory.Packages.props` (versões centralizadas) existem.
- [ ] `docker compose up` sobe `api`, `postgres:17`, `redis:8` e `mailpit`. `GET /health/live` = 200 e `GET /health/ready` = 200 só com Postgres e Redis no ar.
- [ ] `CatalogDb` abre conexão a partir de `ConnectionStrings:Catalog`.
- [ ] `ITenantDatabaseFactory.Create(slug)` gera a connection string a partir do template `TenantDatabase:ConnectionTemplate` substituindo `{slug}`; o slug é validado por `^[a-z0-9-]{3,40}$` antes de qualquer uso.
- [ ] `ITenantContext` (scoped) expõe `TenantId`, `Slug` e `ActorType` e é preenchido pelo middleware de resolução (JWT `tid` ou API key).
- [ ] Hangfire configurado com storage PostgreSQL no schema `hangfire` do Catálogo; dashboard em `/hangfire` acessível só a `PlatformAdmin`.
- [ ] Outbox: tabela `outbox.messages`; `IOutbox.Add(event)` grava na mesma transação do `SaveChanges`; um job recorrente processa as mensagens pendentes.
- [ ] Erros não tratados viram ProblemDetails 500 sem stack trace fora de Development; erros de validação viram 400 com `errors`.
- [ ] Logs estruturados em JSON com `CorrelationId` (header `X-Correlation-Id` aceito ou gerado e devolvido na resposta).
- [ ] OpenAPI em `/openapi/v1.json` e UI Scalar em `/scalar` (somente Development).
- [ ] Os testes de arquitetura passam: nenhum módulo referencia tipos não-`Contracts` de outro módulo.

## Out of scope (explicit)

- Regras de negócio de qualquer módulo.
- Deploy em nuvem, Kubernetes, OpenTelemetry/tracing distribuído.

## Data contracts

- **Configuração (appsettings):**
  - `ConnectionStrings:Catalog` — ex.: `Host=postgres;Database=subscribely_catalog;Username=app;Password=***`
  - `ConnectionStrings:PostgresAdmin` — usuário com `CREATEDB`, usado só pelo provisionamento
  - `TenantDatabase:ConnectionTemplate` — ex.: `Host=postgres;Database=subscribely_t_{slug};Username=app;Password=***`
  - `ConnectionStrings:Redis`, `Smtp:Host`, `Smtp:Port`, `Jwt:*`, `Webhooks:FakeGateway:Secret`
  - Segredos só por variável de ambiente ou user-secrets; nunca commitados.
- **Schemas no Catálogo:** `tenancy`, `billing`, `payments`, `metering`, `notifications`, `audit`, `outbox`, `hangfire`.
- **`outbox.messages`:** `id uuid PK, type text, payload jsonb, occurred_at timestamptz, processed_at timestamptz null, attempts int, last_error text null`. Índice parcial em `processed_at IS NULL`.
- **External dependencies:** PostgreSQL, Redis, Mailpit (SMTP).

## Authorization

- `/health/*`: anônimo. `/hangfire`: somente `PlatformAdmin` (cookie de dashboard derivado do JWT).
- Sem permissão: 401 sem token; 403 com ProblemDetails genérico.

## Edge cases and failure modes

| Caso | Comportamento esperado |
|---|---|
| Redis indisponível | `/health/ready` = 503; a API continua servindo endpoints que não usam Redis |
| Postgres indisponível na inicialização | A API sobe; `ready` = 503 até a conexão voltar (sem crash loop) |
| Slug com caractere inválido no template | Exceção de validação antes de montar a connection string (anti-injeção) |
| Mensagem de outbox falha 5 vezes | Fica com `last_error` e para de ser reprocessada; aparece no log como erro |
| `X-Correlation-Id` maior que 64 chars ou com caracteres inválidos | Ignorado; um novo id é gerado |

## Decisions taken

| Decisão | Alternativa rejeitada | Por quê |
|---|---|---|
| Um projeto por módulo, tipos `internal` + `Contracts` públicos | Projetos `.Contracts` separados | Menos projetos; a fronteira é garantida pelo compilador + teste de arquitetura |
| Outbox próprio em tabela + job Hangfire | MassTransit/Wolverine | YAGNI: monolito sem broker |
| Validação nativa de Minimal APIs (.NET 10 `AddValidation`) | FluentValidation | Zero dependência para regras simples |
| Scalar como UI de OpenAPI | Swashbuckle | O template .NET 10 já usa Microsoft.AspNetCore.OpenApi |

## Constraints

- Segurança: connection strings e segredos fora do git; usuário `app` sem `CREATEDB`/superuser.
- Performance: `ready` responde em < 1 s.

## Open questions (risks)

- Nenhuma. Resolvidas: OpenTelemetry fica fora da v1; as imagens usam tags fixas (`postgres:17`, `redis:8` e a versão estável do Mailpit, definida na T06).
