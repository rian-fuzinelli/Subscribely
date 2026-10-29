# Spec — 03 Subscriptions (ciclo de vida da assinatura)

> Status: **APROVADO** — execução autônoma autorizada pelo humano em 2026-10-28. Herda as decisões de `specs/OVERVIEW.md`.

## Context and problem

Cada tenant tem exatamente uma assinatura, que passa por trial, conversão, mudanças de plano,
cancelamento e inadimplência. As transições têm regras de dinheiro e de acesso. Uma transição
errada cobra indevidamente ou libera o produto sem pagamento.

## Máquina de estados

```
            TenantProvisioned
                   │
                   ▼
              ┌─────────┐ fim do trial sem meio de pagamento ┌─────────┐
              │Trialing │ ─────────────────────────────────▶ │ Expired │
              └────┬────┘                                    └────┬────┘
   fim do trial c/ │ meio de pagamento (fatura TrialConversion)   │ reactivate
                   ▼                                              ▼
              ┌────────┐ pagamento falhou ┌─────────┐ D+7 falhou ┌───────────┐
      ┌─────▶ │ Active │ ───────────────▶ │ PastDue │ ─────────▶ │ Suspended │
      │       └───┬────┘ ◀─────────────── └─────────┘            └─────┬─────┘
      │ pago      │  fim do período com            pago                │ pago
      │           │  cancel_at_period_end                              │
      │           ▼                                                    │
      │      ┌──────────┐ ◀── 30 dias suspensa (fatura → Void)         │
      │      │ Canceled │                                              │
      │      └──────────┘                                              │
      └────────────────────────────────────────────────────────────────┘
```

Produto (endpoints de API key) liberado em: `Trialing`, `Active`, `PastDue`. Bloqueado em: `Expired`, `Suspended`, `Canceled`.

## Acceptance criteria (verifiable)

- [ ] O evento `TenantProvisioned` cria a assinatura `Trialing` com `trial_ends_at = now + 14 dias` e `trial_used = true`; publica `SubscriptionStarted`.
- [ ] D-3 do fim do trial: o job diário publica `TrialEnding` uma única vez por assinatura.
- [ ] Fim do trial **com** meio de pagamento padrão: emite a fatura `TrialConversion` (spec 04), status `Active`, novo período `[trial_ends_at, +1 intervalo)`.
- [ ] Fim do trial **sem** meio de pagamento: status `Expired`; o produto devolve 402.
- [ ] `POST /v1/subscription/change-plan`:
  - **upgrade** (preço diário normalizado maior, ou mensal → anual): aplica na hora, emite a fatura `Upgrade` com proração (spec 04) e invalida o cache de entitlements;
  - **downgrade** (preço diário menor, ou anual → mensal): grava `pending_plan_version_id`/`pending_interval`, aplicado no fim do período;
  - **mesmo preço diário:** aplica na hora sem fatura;
  - **durante o trial:** qualquer mudança aplica na hora, sem fatura, e o trial continua.
- [ ] Agendar um downgrade exige `assentos ativos ≤ max_seats do plano alvo`; senão, 409 `seats-exceed-target-plan` com `{ activeSeats, targetMaxSeats }`. Com um downgrade pendente, o limite para novos convites é `min(atual, alvo)`.
- [ ] `DELETE /v1/subscription/pending-change` cancela o downgrade agendado.
- [ ] `POST /v1/subscription/cancel` define `cancel_at_period_end = true` e remove a mudança pendente; `POST /v1/subscription/resume` desfaz o cancelamento antes do fim do período.
- [ ] No fim do período, o job aplica, nesta ordem: cancelamento → `Canceled`; senão, mudança pendente; senão, **migração para a versão vigente do plano** (se houver uma nova e o plano não estiver arquivado); depois aciona a renovação (spec 04).
- [ ] `SuspensionExpiryJob` (diário): assinatura `Suspended` há 30 dias → `Canceled`; a fatura em aberto é anulada (`Void`, motivo `suspension-expired`); publica `SubscriptionCanceled`.
- [ ] `Canceled`/`Expired` **preservam** o banco do tenant e os usuários; o acesso ao produto fica bloqueado (402) e a reativação recupera tudo. Login e endpoints de billing continuam disponíveis para o TenantAdmin poder reativar.
- [ ] `POST /v1/subscription/reactivate` (de `Expired` ou `Canceled`, com meio de pagamento) inicia um novo período cheio e emite a fatura `Renewal`; não há novo trial.
- [ ] O evento `InvoicePaid` leva `PastDue`/`Suspended` → `Active`; `PaymentRetriesExhausted` leva → `Suspended` (spec 05).
- [ ] Toda transição grava auditoria (spec 08) e publica o evento correspondente no outbox.
- [ ] Atualizações concorrentes na mesma assinatura: a segunda recebe 409 `concurrency-conflict` (concorrência otimista via `xmin`).

## Out of scope (explicit)

- Pausar a assinatura, múltiplas assinaturas por tenant, add-ons.

## Data contracts

- **`GET /v1/subscription`** → `{ status, plan: { code, name, version }, interval, trialEndsAt?, currentPeriodStart, currentPeriodEnd, cancelAtPeriodEnd, pendingChange?: { planCode, interval, effectiveAt } }`
- **`POST /v1/subscription/change-plan`** in: `{ planCode, interval }` → 200 `{ effective: "immediate"|"period_end", invoiceId? }`
- **Catálogo `billing.subscriptions`:** `id, tenant_id UNIQUE, plan_version_id FK, interval[Monthly|Annual], status, trial_ends_at NULL, trial_used bool, current_period_start, current_period_end, cancel_at_period_end bool, pending_plan_version_id NULL, pending_interval NULL, suspended_at NULL, xmin (concurrency)`
- **Eventos:** `SubscriptionStarted`, `TrialEnding`, `SubscriptionChanged`, `SubscriptionCanceled`, `SubscriptionExpired`, `SubscriptionSuspended`, `SubscriptionReactivated` (todos com `tenantId`, `subscriptionId`, `occurredAt`).

## Authorization

- Escrita: TenantAdmin do próprio tenant. Leitura: TenantAdmin e Member.
- `GET /admin/subscriptions?status=`: PlatformAdmin.

## Edge cases and failure modes

| Caso | Comportamento esperado |
|---|---|
| Mudar para o mesmo plano e intervalo | 422 `no-change` |
| Mudança de plano com status `Suspended`/`Expired`/`Canceled` | 409 `subscription-not-changeable` |
| Upgrade com status `PastDue` | 409 (pagar a fatura em aberto primeiro) |
| Job de fim de período roda 2× para o mesmo período | Segunda execução é no-op (checa `current_period_end`) |
| Plano alvo arquivado | 422 `plan-unavailable` |
| Cancelar já cancelada | 200 idempotente |

## Decisions taken

| Decisão | Alternativa rejeitada | Por quê |
|---|---|---|
| "Upgrade" definido pelo preço diário normalizado | Comparar pelo nome/ordem do plano | Cobre a troca de intervalo sem tabela de hierarquia |
| Mudança no trial é imediata e grátis | Proibir mudança no trial | Experiência de avaliação |
| Bloquear downgrade com excesso de seats no agendamento | Desativar usuários automaticamente | Nunca remover acesso de pessoas sem ação humana |

## Constraints

- Todas as datas em UTC via `TimeProvider`; o período mensal soma 1 mês de calendário (31/01 → 28/02 → 31/03, ancorado no dia original).

## Open questions (risks)

- Nenhuma. Resolvidas: `Suspended` por 30 dias → `Canceled` com a fatura anulada; os dados do tenant são preservados (sem exclusão na v1); a nova versão do plano é aplicada na renovação.
