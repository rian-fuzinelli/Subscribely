---
name: subscribely-billing-domain
description: Regras de negócio de billing do Subscribely — planos versionados, máquina de estados da assinatura (trial, upgrade, downgrade, cancelamento, dunning, suspensão), períodos mensais/anuais, faturas imutáveis e proração por segundos em centavos. Use ao implementar ou testar qualquer coisa nos módulos Billing (planos, assinaturas, faturas).
---

# Domínio de billing — Subscribely

## Invariantes (nunca violar)
- Um tenant tem **uma** assinatura. Trial: 14 dias, 1 por tenant (`trial_used`).
- Toda transição de status passa por um método do agregado `Subscription` que devolve `Result` e registra um evento de domínio. **Proibido** setar `Status` fora do agregado.
- Fatura é **imutável** após a emissão (só o status e os campos de pagamento mudam). `total_cents == Σ linhas`.
- Dinheiro em `long` (centavos). Proibido `double`/`float`/`decimal` em cálculo de billing.
- Tempo via `TimeProvider`. Períodos mensais ancorados no dia original (31/01 → 28/02 → 31/03).

## Máquina de estados
| De | Evento/ação | Para |
|---|---|---|
| — | `TenantProvisioned` | Trialing |
| Trialing | fim do trial c/ meio de pagamento | Active (+ fatura TrialConversion) |
| Trialing | fim do trial s/ meio de pagamento | Expired |
| Active | `PaymentFailed` | PastDue |
| PastDue | `InvoicePaid` | Active |
| PastDue | `PaymentRetriesExhausted` | Suspended |
| Suspended | `InvoicePaid` | Active |
| Active/PastDue | fim do período c/ `cancel_at_period_end` | Canceled |
| Expired/Canceled | `reactivate` (c/ meio de pagamento) | Active (+ fatura Reactivation) |
Qualquer outra combinação → `Error.Conflict("subscription-not-changeable")`.

## Mudança de plano (`PlanChangeClassifier`)
- `diário(plano) = preço do intervalo / segundos do intervalo` (compare como frações com `long`, sem ponto flutuante: `a.preço × b.duração` vs `b.preço × a.duração`).
- Maior → **upgrade imediato** (fatura `Upgrade` com proração); menor → **downgrade no fim do período**; igual → imediato sem fatura.
- Mensal → anual = upgrade (novo período anual começa agora). Anual → mensal = downgrade.
- Em `Trialing`: tudo imediato e sem fatura; o trial continua.
- Downgrade exige `seats ativos ≤ max_seats do alvo` (via `ITenancyApi.CountActiveSeats`).

## Proração (`ProrationCalculator`, função pura)
```
restante = period_end - now      (segundos)
total    = period_end - period_start
crédito  = -RoundHalfUp(preço_antigo × restante / total)
cobrança = +RoundHalfUp(preço_novo   × restante / total)
```
Use `Int128` ou `Math.BigMul` para evitar overflow. Total ≤ 0 → fatura `Paid` de 0.

## Idempotência
- Jobs de período e consumidores checam o estado atual antes de agir (rodar 2× = no-op).
- Fatura de período: `UNIQUE(subscription_id, period_start, reason)`.

## Testes exigidos
- 100% de cobertura de transições (válidas e inválidas) e do `ProrationCalculator` (testes de tabela + propriedade: |crédito| ≤ preço antigo; soma das linhas = total).

## Skills relacionadas
`dotnet-claude-kit:ddd`, `backend-design:idempotency-and-side-effects`.
