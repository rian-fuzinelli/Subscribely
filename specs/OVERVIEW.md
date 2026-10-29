# Subscribely — Visão geral do produto e mapa de módulos

> Documento-raiz do SDD. Toda spec de módulo herda as decisões daqui; contradições devem
> ser resolvidas **aqui primeiro**. Fluxo de trabalho: `specs/README.md`. Agentes: `specs/ORCHESTRATION.md`.

## Problema

Subscribely é o backend de assinaturas e faturamento de **um** SaaS. Cada **tenant** é uma
organização cliente que assina um plano, tem limites de uso (requisições/mês e assentos),
é cobrada em ciclos mensais ou anuais e passa por trial, upgrade, downgrade, cancelamento e
inadimplência. O projeto demonstra arquitetura SaaS, segurança, processamento em background e
regras de negócio de assinatura.

## Atores

| Papel | Escopo | Pode |
|---|---|---|
| `PlatformAdmin` | Plataforma (sem tenant) | Gerenciar planos, consultar tenants/assinaturas/auditoria global |
| `TenantAdmin` | Próprio tenant | Gerenciar assinatura, meio de pagamento, usuários, API keys; ver faturas, uso, auditoria |
| `Member` | Próprio tenant | Somente leitura: tenant, assinatura, uso |
| API key | Próprio tenant | Chamar endpoints de **produto** (`/v1/projects`), que consomem quota |

## Critério de sucesso global (definição de "pronto")

- [x] `docker compose up` sobe API, PostgreSQL, Redis e Mailpit; a API responde `GET /health/ready` = 200.
- [x] Testes de integração (Testcontainers) cobrem: trial → pago → upgrade → downgrade → cancelamento;
      webhook duplicado sem efeito; dunning até suspensão; isolamento entre tenants; quota 429.
- [x] Testes de arquitetura garantem as fronteiras entre módulos.

## Decisões globais

| Tema | Decisão | Alternativa rejeitada | Por quê |
|---|---|---|---|
| Arquitetura | **Modular Monolith + Vertical Slice** | Clean Architecture em 4 camadas | Fronteiras por módulo = cada agente dono de um módulo, merges sem conflito |
| Endpoints | Minimal APIs, um `Endpoint` por slice, prefixo `/v1` | Controllers do template | Coesão por feature; o template será limpo na fundação |
| Multi-tenancy | **Banco por tenant** numa única instância Postgres + **banco Catálogo** | Linha com `TenantId` / schema por tenant | Escolha do produto: isolamento físico dos dados de produto |
| Conexões | appsettings guarda `ConnectionStrings:Catalog` + template `Database=subscribely_t_{slug}` | Um appsettings por tenant | Signup self-service; nenhuma senha por tenant em arquivo |
| Control vs data plane | Catálogo: tenants, usuários, API keys, planos, assinaturas, faturas, pagamentos, outbox, auditoria de controle, Hangfire. Tenant DB: dados de produto + auditoria de produto | Tudo por tenant | Jobs de cobrança varrem 1 banco; login não precisa iterar N bancos |
| Autenticação | JWT próprio (access 15 min) + refresh token rotativo (7 dias) | Keycloak / Identity API | Autocontido; demonstra segurança de ponta a ponta |
| Jobs | **Hangfire** com storage PostgreSQL (schema `hangfire` no Catálogo) | Quartz.NET | Dashboard, retries e recorrência prontos |
| Cache/contadores | Redis (quota, cache de API key) | Contador no Postgres | Incremento atômico de baixa latência |
| Pagamentos | `IPaymentGateway` + **FakeGateway** com webhooks assinados com HMAC | Stripe test mode | Sem dependência externa; testável; trocável depois |
| E-mail | SMTP → **Mailpit** (compose), via outbox + job | SendGrid/SES | Envio real capturado localmente, sem custo |
| Dinheiro | Inteiro em centavos (`long`, `*_cents`), moeda única BRL | `decimal` | Sem erro de arredondamento; comparações exatas |
| Tempo | `TimeProvider` injetado em tudo; UTC no banco | `DateTime.UtcNow` direto | Testes de ciclos de cobrança determinísticos |
| Erros | ProblemDetails (RFC 9457) | Corpo de erro próprio | Padrão da plataforma |

## Regras de negócio globais (já aprovadas)

- **Trial:** 14 dias, sem cartão, 1 por tenant.
- **Periodicidade:** mensal e anual; quota de requisições sempre mensal.
- **Upgrade:** imediato com proração diária. **Downgrade:** agendado para o fim do período, sem reembolso.
- **Cancelamento:** no fim do período (`cancel_at_period_end`), reversível até lá, sem reembolso.
- **Dunning:** falha → `PastDue`; retentativas D+1, D+3, D+7; última falha → `Suspended` (produto bloqueado) até pagar.
- **Suspensão longa:** `Suspended` há 30 dias → `Canceled` automaticamente; a fatura em aberto é anulada (`Void`).
- **Dados do tenant:** nunca excluídos na v1; `Canceled`/`Expired` bloqueiam o acesso (402) e a reativação recupera tudo.
- **Cobrança:** imediata na emissão da fatura (sem prazo de vencimento).
- **Nova versão de plano:** quem já assina migra para a versão vigente na próxima renovação.
- **Usuários:** e-mail único global (um usuário pertence a um único tenant); assentos = usuários ativos + convites pendentes.
- **Senha:** recuperação por e-mail com token de uso único (1 h).
- **Quota com Redis fora do ar:** fail-open (libera e registra um warning).
- **E-mails:** trial acabando (D-3), fatura emitida/paga, pagamento falhou/suspensão, quota em 80% e 100% (só TenantAdmins), além dos funcionais: convite e recuperação de senha.

## Fora de escopo (v1)

- Cupons e descontos.
- Impostos, nota fiscal e multi-moeda (somente BRL).
- Frontend/portal (spec própria no futuro).
- OpenTelemetry/tracing (v1 = logs estruturados + health checks).
- Retenção e expurgo de dados (auditoria e bancos de tenant ficam indefinidamente; LGPD vira spec futura).

## Layout da solução (alvo)

```
src/
  Subscribely.Api/                          host: Program.cs, composição dos módulos, middlewares
  BuildingBlocks/
    Subscribely.SharedKernel/               Result, erros, Entity, Money (cents), abstrações de outbox/auditoria
    Subscribely.Infrastructure/             EF base, resolução de tenant, Catálogo/TenantDb, Redis, Hangfire, Outbox, interceptor de auditoria
  Modules/
    Subscribely.Modules.Tenancy/            signup, provisionamento, usuários, auth JWT, RBAC, API keys
    Subscribely.Modules.Billing/            planos, assinaturas, ciclos, faturas, proração
    Subscribely.Modules.Payments/           gateway, meios de pagamento, cobranças, webhooks, dunning
    Subscribely.Modules.Metering/           quota em Redis, enforcement, alertas de uso
    Subscribely.Modules.Notifications/      templates e envio de e-mail (consumidor do outbox)
    Subscribely.Modules.Audit/              trilha de auditoria (escrita e consulta)
    Subscribely.Modules.Projects/           produto demo (data plane) que consome quota
tests/
  Subscribely.UnitTests/                    pastas por módulo
  Subscribely.IntegrationTests/             WebApplicationFactory + Testcontainers (Postgres, Redis)
  Subscribely.ArchitectureTests/            fronteiras entre módulos (NetArchTest)
Subscribely.Workspace/                      vitrine do Visual Studio (specs, .claude, .specify)
docker-compose.yml
```

**Regra de fronteira:** dentro de cada módulo, tudo é `internal`, exceto o namespace
`Subscribely.Modules.<X>.Contracts` (interfaces e eventos públicos) e o método `Add<X>Module()`.
Módulos conversam apenas via `Contracts` (síncrono) ou eventos no outbox (assíncrono).

## Mapa de specs e ondas de execução

| # | Spec | Módulo(s) | Depende de | Onda |
|---|---|---|---|---|
| 00 | [foundation](00-foundation/spec.md) | Api, BuildingBlocks, compose | — | 0 |
| 01 | [tenancy-identity](01-tenancy-identity/spec.md) | Tenancy | 00 | 1 |
| 02 | [plans](02-plans/spec.md) | Billing | 00 | 1 |
| 08 | [audit-trail](08-audit-trail/spec.md) | Audit | 00 | 1 |
| 03 | [subscriptions](03-subscriptions/spec.md) | Billing | 01, 02 | 2 |
| 06 | [metering](06-metering/spec.md) | Metering, Projects | 01, 02 | 2 |
| 07 | [notifications](07-notifications/spec.md) | Notifications | 00 | 2 |
| 04 | [invoicing](04-invoicing/spec.md) | Billing | 03 | 3 |
| 05 | [payments-webhooks](05-payments-webhooks/spec.md) | Payments | 04 | 3 |
| 09 | [e2e-quality](09-e2e-quality/spec.md) | tests, CI | todas | 4 |

Specs da mesma onda podem rodar **em paralelo** (ver `ORCHESTRATION.md`).
