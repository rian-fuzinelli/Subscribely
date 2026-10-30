# Progresso — execução autônoma

> **Como retomar (nova sessão):** leia este arquivo → abra o `tasks.md` da spec ativa →
> `git switch <branch ativa>` → continue a primeira tarefa `[ ]`. Não pergunte nada ao humano:
> decida pela opção mais simples coerente com a spec e registre a decisão abaixo.

## Estado atual

- **Spec ativa:** nenhuma — todas as specs (00–10) estão concluídas e integradas em `develop` e `main`
- **Branch ativa:** `develop`
- **Próxima tarefa:** com um remoto configurado, publicar as branches e abrir os PRs a partir de `docs/prs/`; rodar a CI no GitHub Actions

## Ordem de execução

00 → 02 → 08 → 01 → 03 → 04 → 05 → 06 → 07 → 09 (sequencial; 02 antes de 01 porque o signup valida o plano; 07 por último entre os módulos porque consome eventos de 03–06).

## Desvios do fluxo padrão (decisões autônomas)

| Data | Decisão | Motivo |
|---|---|---|
| 2026-10-28 | Sem `origin` e sem `gh`: a fase *close* faz `git merge --no-ff` da branch em `develop` localmente e salva o rascunho do PR em `docs/prs/<branch>.md` | Não há repositório remoto configurado; o humano ordenou autonomia total |
| 2026-10-28 | Os agentes customizados (`.claude/agents`) só carregam em uma sessão nova; nesta sessão o orquestrador implementa diretamente, seguindo as mesmas skills | Os subagent types não estavam disponíveis na sessão em que foram criados |
| 2026-10-28 | O git usa `core.autocrlf=false` neste repositório | Evita diffs de fim de linha em massa |
| 2026-10-28 | Outbox despachado por `BackgroundService` (polling de 2 s) em vez de job recorrente do Hangfire | Cron do Hangfire tem granularidade mínima de 1 min |
| 2026-10-28 | Validação por filtro DataAnnotations (`WithRequestValidation<T>()`) em vez de `AddValidation()` | O gerador do .NET 10 não cobre endpoints em bibliotecas de módulo |
| 2026-10-28 | Validação de JWT e políticas RBAC ficam em `Infrastructure/Security`; a emissão de tokens fica no módulo Tenancy | Todos os módulos precisam das políticas antes da spec 01 |
| 2026-10-29 | Contratos de Billing e Tenancy viraram projetos `*.Contracts` separados | Evita referência circular entre módulos |
| 2026-10-29 | Testes de integração usam um banco Catálogo isolado por factory | O outbox compartilhado causava interferência entre classes de teste |
| 2026-10-28 | Specs executadas em sequência pelo orquestrador (sem subagentes paralelos) | Os agentes customizados não estão carregados nesta sessão; a sequência evita conflitos de merge |
| 2026-10-29 | Ordem alterada para 03 → 04 → 05 → 06 → 07 | Notificações (07) consomem eventos de faturas, pagamentos e quota; implementá-las antes exigiria stubs |
| 2026-10-29 | Assinatura ganhou `billing_anchor` e `trial_ending_notified_at` (fora do contrato de dados da spec 03) | Âncora do dia de cobrança e aviso D-3 único exigem estado persistido |
| 2026-10-29 | Novo projeto `Payments.Contracts` (`IPaymentsApi`, `PaymentFailed`, `PaymentRetriesExhausted`) | Billing consulta o meio de pagamento e consome os eventos de dunning sem depender do módulo Payments |

## Specs concluídas

| Spec | Branch | Merge em develop | Testes |
|---|---|---|---|
| 00-foundation | `chore/foundation-modular-monolith` | 2026-10-28 | unit 39 · arch 15 · integration 13 · smoke do compose ok |
| 02-plans | `feat/billing-plans` | 2026-10-28 | unit 47 · integration 22 (3 execuções verdes) |
| 08-audit-trail | `feat/audit-trail` | 2026-10-28 | arch 17 · integration 29 |
| 01-tenancy-identity | `feat/tenancy-identity` | 2026-10-29 | unit 47 · arch 17 · integration 51 |
| 03-subscriptions (+02-T05) | `feat/billing-subscriptions` | 2026-10-29 | unit 107 · arch 17 · integration 68 |
| 04-invoicing | `feat/billing-invoicing` | 2026-10-29 | unit 126 · arch 17 · integration 76 |
| 05-payments-webhooks | `feat/payments-webhooks` | 2026-10-29 | unit 153 · arch 17 · integration 85 |
| 06-metering | `feat/metering-quota` | 2026-10-29 | unit 163 · arch 17 · integration 95 |
| 07-notifications | `feat/email-notifications` | 2026-10-29 | unit 182 · arch 17 · integration 104 |
| 09-e2e-quality | `test/e2e-scenarios` | 2026-10-29 | unit 182 · arch 17 · integration 121 (10 E2E) · cobertura 93% |
| 10-web-portal | `feat/web-portal` | 2026-10-30 | API: unit 182 · arch 17 · integration 122 · portal: 29 testes (Vitest), lint, typecheck e build |

## Pendências para o humano (não bloqueiam)

- Abrir os PRs no GitHub a partir dos rascunhos em `docs/prs/` (as branches já foram integradas em `develop`).
