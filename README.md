# Subscribely

API de **assinaturas e faturamento multi-tenant** para SaaS, em .NET 10: planos com limites de uso,
ciclos de cobrança, trial, upgrade/downgrade, cancelamento, eventos de pagamento idempotentes,
jobs agendados e trilha de auditoria.

> Projeto de portfólio construído com **Spec-Driven Development** — cada funcionalidade nasce de uma
> spec aprovada em [`specs/`](specs/OVERVIEW.md) antes de virar código.

**Stack:** .NET 10 · ASP.NET Core Minimal APIs · EF Core 10 · PostgreSQL 17 · Redis 8 · Hangfire · Docker · xUnit v3 + Testcontainers

---

## Como rodar

```bash
cp .env.example .env            # ajuste as senhas e a JWT_SIGNING_KEY
docker compose up -d --build    # api, postgres, redis, mailpit
```

| Endereço | O quê |
|---|---|
| http://localhost:3000 | Portal web dos clientes ([`web/`](web/README.md)) |
| http://localhost:8080/scalar | Documentação interativa da API |
| http://localhost:8080/health/ready | Prontidão (Postgres + Redis) |
| http://localhost:8080/hangfire | Dashboard de jobs (PlatformAdmin) |
| http://localhost:8025 | E-mails capturados (Mailpit) |

```bash
dotnet test --project tests/Subscribely.UnitTests
dotnet test --project tests/Subscribely.ArchitectureTests
dotnet test --project tests/Subscribely.IntegrationTests   # requer Docker (inclui os 10 cenários E2E)
```

Cobertura (a CI exige ≥ 90% em Billing e Payments e 100% em `ProrationCalculator`, `Subscription` e `WebhookSignature`):

```bash
dotnet tool restore
dotnet test --project tests/Subscribely.UnitTests -- --coverage --coverage-output-format cobertura --coverage-settings coverage.settings.xml --coverage-output "$PWD/TestResults/coverage/unit.cobertura.xml"
dotnet test --project tests/Subscribely.IntegrationTests -- --coverage --coverage-output-format cobertura --coverage-settings coverage.settings.xml --coverage-output "$PWD/TestResults/coverage/integration.cobertura.xml"
dotnet reportgenerator "-reports:TestResults/coverage/*.cobertura.xml" "-targetdir:TestResults/coverage/report" "-reporttypes:Cobertura;HtmlInline"
python3 scripts/check-coverage.py TestResults/coverage/report/Cobertura.xml
```

Para experimentar a cobrança no compose: `PUT /v1/billing/payment-method` com um token de teste do gateway fake
(`pm_card_ok`, `pm_card_declined`, `pm_card_insufficient_funds`, `pm_card_fail_then_ok`) e `/v1/projects` com o
header `X-Api-Key` (criada em `POST /v1/api-keys`).

---

## Decisões de arquitetura (ADRs)

Formato: **Contexto → Decisão → Consequências**. Todas com status **Aceita**.

### ADR-001 — Monolito modular com vertical slices

- **Contexto:** o domínio tem fronteiras claras (identidade, billing, pagamentos, uso, notificações, auditoria), mas o time é pequeno e microserviços trariam custo operacional sem benefício.
- **Decisão:** um projeto por módulo; tudo `internal` exceto `Contracts` (interfaces e eventos). Cada feature é um slice (endpoint + handler) dentro do módulo.
- **Consequências:** fronteiras garantidas pelo compilador e por testes de arquitetura (NetArchTest); módulos conversam só via contratos síncronos ou eventos. Extrair um serviço no futuro é mover um projeto.

### ADR-002 — Banco por tenant + banco Catálogo

- **Contexto:** vazamento de dados entre clientes é a falha mais grave de um SaaS.
- **Decisão:** cada tenant tem seu próprio banco (`subscribely_t_{slug}`) na mesma instância Postgres, criado automaticamente no signup. Um banco **Catálogo** guarda o *control plane* (tenants, usuários, planos, assinaturas, faturas, pagamentos, outbox, auditoria).
- **Consequências:** isolamento físico dos dados de produto; cobrança e login consultam um único banco. Conexões vêm de um template no `appsettings` (sem senha por tenant em arquivo). Filtros globais do EF reforçam o isolamento no Catálogo.

### ADR-003 — JWT próprio + refresh rotativo; API keys para o produto

- **Contexto:** usuários gerenciam a conta; sistemas do cliente consomem o produto com quota.
- **Decisão:** access token JWT de 15 min + refresh token rotativo com **detecção de reuso por família**. Endpoints de produto aceitam só API key (`sk_live_…`, guardada como SHA-256, exibida uma vez).
- **Consequências:** autocontido (sem IdP externo); RBAC com `PlatformAdmin`, `TenantAdmin` e `Member`; recurso de outro tenant responde **404** (não revela existência).

### ADR-004 — Outbox transacional + Hangfire

- **Contexto:** mudanças de estado precisam disparar efeitos (provisionar banco, enviar e-mail, cobrar) sem perder eventos quando algo falha.
- **Decisão:** eventos gravados em `outbox.messages` **na mesma transação** da mudança; um despachante entrega aos consumidores. Trabalho pesado ou agendado roda no Hangfire (storage no Postgres).
- **Consequências:** entrega *at-least-once* → todo consumidor e job é idempotente. Migrations e schema do Hangfire rodam sob *advisory lock* (seguro com várias réplicas).

### ADR-005 — Gateway de pagamento fake com webhooks assinados

- **Contexto:** gateways reais exigem conta e acoplam testes a serviços externos.
- **Decisão:** abstração `IPaymentGateway` com um provider fake que envia webhooks **HTTP reais** assinados com HMAC-SHA256. Recepção *insert-first* com chave única por evento.
- **Consequências:** idempotência, ordem e assinatura testáveis localmente; trocar por Stripe é implementar a interface.

### ADR-006 — Dinheiro em centavos e tempo injetado

- **Contexto:** proração e ciclos de cobrança não toleram erros de arredondamento nem testes dependentes do relógio.
- **Decisão:** valores em `long` (centavos, BRL) com arredondamento *half-up* explícito; todo código usa `TimeProvider`.
- **Consequências:** cálculos exatos; testes avançam o tempo com `FakeTimeProvider` (trial de 14 dias, dunning D+1/D+3/D+7).

### ADR-007 — Auditoria append-only na mesma transação

- **Contexto:** billing e segurança exigem saber quem fez o quê, quando e de onde.
- **Decisão:** interceptor do EF grava mudanças de entidades `[Auditable]` e ações explícitas na mesma transação; campos sensíveis marcados `[NotAudited]`. Trigger + `REVOKE` impedem `UPDATE`/`DELETE`.
- **Consequências:** nada muda sem trilha; um teste de arquitetura obriga `[NotAudited]` em campos de hash/token/segredo.

### ADR-008 — Spec-Driven Development com agentes especialistas

- **Contexto:** o projeto é construído com IA; código sem especificação tende a divergir.
- **Decisão:** cada módulo tem `spec.md` → `plan.md` → `tasks.md` em [`specs/`](specs/); 1 tarefa = 1 commit. Agentes especialistas (arquitetura, banco, segurança, billing, integrações, jobs, notificações, testes, DevOps, revisão) usam skills próprias em [`.claude/skills/`](.claude/skills/).
- **Consequências:** decisões rastreáveis até a spec; progresso retomável entre sessões via [`specs/PROGRESS.md`](specs/PROGRESS.md).

---

## Estrutura

```
src/
  Subscribely.Api/              host: composição, middlewares, health, OpenAPI
  BuildingBlocks/               SharedKernel (Result, Money...) e Infrastructure (EF, outbox, jobs, auth, auditoria)
  Modules/                      Tenancy, Billing, Payments, Metering, Notifications, Audit, Projects (+ Contracts)
tests/                          Unit, Architecture e Integration (Testcontainers)
specs/                          specs SDD por módulo, visão geral e progresso
docker-compose.yml
```

## Status

| Módulo | Spec | Situação |
|---|---|---|
| Fundação (infra, outbox, jobs, Docker) | [00](specs/00-foundation/spec.md) | ✅ |
| Planos | [02](specs/02-plans/spec.md) | ✅ |
| Auditoria | [08](specs/08-audit-trail/spec.md) | ✅ |
| Tenancy & identidade | [01](specs/01-tenancy-identity/spec.md) | ✅ |
| Assinaturas | [03](specs/03-subscriptions/spec.md) | ⏳ |
| Notificações por e-mail | [07](specs/07-notifications/spec.md) | ⏳ |
| Quota / metering | [06](specs/06-metering/spec.md) | ⏳ |
| Faturas e proração | [04](specs/04-invoicing/spec.md) | ⏳ |
| Pagamentos e webhooks | [05](specs/05-payments-webhooks/spec.md) | ⏳ |
| Cenários E2E e CI | [09](specs/09-e2e-quality/spec.md) | ⏳ |
