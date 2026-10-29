# Spec — 04 Invoicing (ciclos de cobrança, faturas e proração)

> Status: **APROVADO** — execução autônoma autorizada pelo humano em 2026-10-28. Herda as decisões de `specs/OVERVIEW.md`.

## Context and problem

Cada renovação, conversão de trial, reativação e upgrade precisa gerar uma fatura exata,
imutável e numerada, que a spec 05 cobra. Erros de centavos ou faturas duplicadas são
inaceitáveis em billing.

## Acceptance criteria (verifiable)

- [ ] Fatura emitida pelos motivos `TrialConversion`, `Renewal`, `Upgrade` e `Reactivation`, com linhas (`invoice_lines`) e `total_cents = soma das linhas`.
- [ ] Número sequencial global no formato `SUB-{yyyy}-{000001}`, gerado por sequence do Postgres (sem lacunas por concorrência na mesma transação).
- [ ] **Renovação:** o `PeriodEndJob` (spec 03) emite **uma** fatura por assinatura por período. Unicidade garantida por `UNIQUE(subscription_id, period_start, reason)`.
- [ ] **Proração de upgrade (mesmo intervalo):** `fração = segundos restantes / segundos do período`; linha de crédito `-round(preço_antigo × fração)`; linha de cobrança `+round(preço_novo × fração)`; arredondamento *half-up* para centavos. O período não muda.
- [ ] **Upgrade mensal → anual:** novo período anual começa agora; crédito do mensal não usado + cobrança do anual cheio.
- [ ] Total ≤ 0 → fatura registrada como `Paid` com `total_cents = 0` (sem cobrança, sem crédito transportado).
- [ ] Fatura emitida publica `InvoiceIssued { invoiceId, tenantId, totalCents }`; status final publica `InvoicePaid` ou `InvoiceVoided`.
- [ ] Fatura é imutável após emissão: só o status e os campos de pagamento mudam.
- [ ] `GET /v1/invoices` (paginado por cursor, mais recentes primeiro) e `GET /v1/invoices/{id}` com as linhas.
- [ ] `POST /admin/invoices/{id}/void` anula fatura `Open` (auditado, com motivo obrigatório).
- [ ] O sistema também anula a fatura em aberto quando a suspensão expira (spec 03), com o motivo `suspension-expired` e ator `System(SuspensionExpiryJob)`.

## Out of scope (explicit)

- Impostos, NF, PDF da fatura, notas de crédito, saldo/crédito transportado, multi-moeda.

## Data contracts

- **Catálogo `billing`:**
  - `invoices(id uuid, number text UNIQUE, tenant_id, subscription_id, reason[TrialConversion|Renewal|Upgrade|Reactivation], status[Open|Paid|Void], period_start, period_end, total_cents bigint, currency char(3)='BRL', issued_at, paid_at NULL, voided_at NULL, void_reason NULL, UNIQUE(subscription_id, period_start, reason))`
  - `invoice_lines(id, invoice_id FK, description, plan_version_id, amount_cents bigint (negativo = crédito), period_start, period_end)`
- **`GET /v1/invoices/{id}`** → `{ id, number, reason, status, periodStart, periodEnd, totalCents, currency, issuedAt, paidAt?, lines: [{ description, amountCents, periodStart, periodEnd }] }`
- **Contracts:** `IBillingApi.GetInvoice(invoiceId)`, `IBillingApi.MarkInvoicePaid(invoiceId, paidAt)` (chamado pela spec 05).

## Authorization

- Leitura: TenantAdmin do próprio tenant (Member não vê faturas). Void: PlatformAdmin.

## Edge cases and failure modes

| Caso | Comportamento esperado |
|---|---|
| Upgrade no último segundo do período | Fração ≈ 0 → total 0 → `Paid` sem cobrança |
| Dois upgrades no mesmo período | Duas faturas `Upgrade` (cada uma prorrateia a partir do plano vigente) |
| Job de renovação duplicado | Violação do UNIQUE é tratada como no-op |
| `MarkInvoicePaid` numa fatura `Void` | Ignorado + log de alerta (pagamento a reconciliar manualmente) |
| `MarkInvoicePaid` repetido | Idempotente |

## Decisions taken

| Decisão | Alternativa rejeitada | Por quê |
|---|---|---|
| Proração por segundos | Por dias inteiros | Exatidão sem regra de "dia parcial"; fácil de testar com `FakeTimeProvider` |
| Sem crédito transportado | Saldo de crédito | Downgrade é no fim do período; o crédito só existe dentro de uma fatura de upgrade |
| Número por sequence do Postgres | Contador em tabela | Sem lock de linha; ordem global |

## Constraints

- Toda aritmética em `long` (centavos). Proibido `double`/`float`.

## Open questions (risks)

- Nenhuma. Resolvida: a cobrança é **imediata na emissão** (sem prazo de vencimento; o dunning conta a partir de `issued_at`).
