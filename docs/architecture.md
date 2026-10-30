# Decisões de arquitetura

Formato: **Contexto → Decisão → Consequências**. Todas com status **Aceita**.

## ADR-001 — Monolito modular com vertical slices

- **Contexto:** o domínio tem fronteiras claras (identidade, billing, pagamentos, uso, notificações, auditoria), mas o time é pequeno e microserviços trariam custo operacional sem benefício.
- **Decisão:** um projeto por módulo; tudo `internal` exceto `Contracts` (interfaces e eventos). Cada feature é um slice (endpoint + handler) dentro do módulo.
- **Consequências:** fronteiras garantidas pelo compilador e por testes de arquitetura (NetArchTest); módulos conversam só via contratos síncronos ou eventos. Extrair um serviço no futuro é mover um projeto.

## ADR-002 — Banco por tenant + banco Catálogo

- **Contexto:** vazamento de dados entre clientes é a falha mais grave de um SaaS.
- **Decisão:** cada tenant tem seu próprio banco (`subscribely_t_{slug}`) na mesma instância Postgres, criado automaticamente no signup. Um banco **Catálogo** guarda o *control plane* (tenants, usuários, planos, assinaturas, faturas, pagamentos, outbox, auditoria).
- **Consequências:** isolamento físico dos dados de produto; cobrança e login consultam um único banco. Conexões vêm de um template no `appsettings` (sem senha por tenant em arquivo). Filtros globais do EF reforçam o isolamento no Catálogo.

## ADR-003 — JWT próprio + refresh rotativo; API keys para o produto

- **Contexto:** usuários gerenciam a conta; sistemas do cliente consomem o produto com quota.
- **Decisão:** access token JWT de 15 min + refresh token rotativo com **detecção de reuso por família**. Endpoints de produto aceitam API key (`sk_live_…`, guardada como SHA-256, exibida uma vez) ou a sessão de um TenantAdmin (playground do portal).
- **Consequências:** autocontido (sem IdP externo); RBAC com `PlatformAdmin`, `TenantAdmin` e `Member`; recurso de outro tenant responde **404** (não revela existência).

## ADR-004 — Outbox transacional + Hangfire

- **Contexto:** mudanças de estado precisam disparar efeitos (provisionar banco, enviar e-mail, cobrar) sem perder eventos quando algo falha.
- **Decisão:** eventos gravados em `outbox.messages` **na mesma transação** da mudança; um despachante entrega aos consumidores. Trabalho pesado ou agendado roda no Hangfire (storage no Postgres).
- **Consequências:** entrega *at-least-once* → todo consumidor e job é idempotente. Migrations e schema do Hangfire rodam sob *advisory lock* (seguro com várias réplicas).

## ADR-005 — Gateway de pagamento fake com webhooks assinados

- **Contexto:** gateways reais exigem conta e acoplam testes a serviços externos.
- **Decisão:** abstração `IPaymentGateway` com um provider fake, chamado por HTTP com retry e idempotência, que envia webhooks **HTTP reais** assinados com HMAC-SHA256. Recepção *insert-first* com chave única por evento.
- **Consequências:** idempotência, ordem e assinatura testáveis localmente; trocar por Stripe é implementar a interface.

## ADR-006 — Dinheiro em centavos e tempo injetado

- **Contexto:** proração e ciclos de cobrança não toleram erros de arredondamento nem testes dependentes do relógio.
- **Decisão:** valores em `long` (centavos, BRL) com arredondamento *half-up* explícito; todo código usa `TimeProvider`.
- **Consequências:** cálculos exatos; testes avançam o tempo com `FakeTimeProvider` (trial de 14 dias, dunning D+1/D+3/D+7).

## ADR-007 — Auditoria append-only na mesma transação

- **Contexto:** billing e segurança exigem saber quem fez o quê, quando e de onde.
- **Decisão:** interceptor do EF grava mudanças de entidades `[Auditable]` e ações explícitas na mesma transação; campos sensíveis marcados `[NotAudited]`. Trigger + `REVOKE` impedem `UPDATE`/`DELETE`.
- **Consequências:** nada muda sem trilha; um teste de arquitetura obriga `[NotAudited]` em campos de hash/token/segredo.

## ADR-008 — Spec-Driven Development

- **Contexto:** o projeto é construído com apoio de IA; código sem especificação tende a divergir.
- **Decisão:** cada módulo tem `spec.md` → `plan.md` → `tasks.md` em [`specs/`](../specs/); 1 tarefa = 1 commit.
- **Consequências:** decisões rastreáveis até a spec; progresso registrado em [`specs/PROGRESS.md`](../specs/PROGRESS.md).

## ADR-009 — Portal web como BFF

- **Contexto:** o portal precisa da API autenticada sem expor tokens a scripts do navegador.
- **Decisão:** Next.js com Server Components e Server Actions; o JWT e o refresh token ficam em cookies `HttpOnly` e só o servidor do portal fala com a API. Código organizado em vertical slices (`web/src/features/<slice>`).
- **Consequências:** nenhum token no JavaScript do navegador; cada tela tem o seu slice com dados, ações e componentes.
