# Spec — 05 Payments, Gateway & Webhooks

> Status: **APROVADO** — execução autônoma autorizada pelo humano em 2026-10-28. Herda as decisões de `specs/OVERVIEW.md`.

## Context and problem

Faturas emitidas precisam ser cobradas num gateway externo, que confirma o resultado de forma
**assíncrona** via webhook. Gateways reais reenviam, atrasam e embaralham eventos, então o
processamento precisa ser autenticado, idempotente e tolerante a ordem. Falhas de pagamento
disparam o dunning.

## Acceptance criteria (verifiable)

- [ ] `IPaymentGateway` com `CreateChargeAsync(invoiceId, amountCents, paymentMethodToken, idempotencyKey)`; implementação `FakePaymentGateway` registrada por configuração (`Payments:Provider = Fake`).
- [ ] O FakeGateway envia webhooks **por HTTP real** para `Payments:WebhookBaseUrl/webhooks/payments/fake` após 1–3 s, assinados com HMAC-SHA256.
- [ ] Tokens de teste do Fake: `pm_card_ok` (sucesso), `pm_card_declined` (`card_declined`), `pm_card_insufficient_funds` (`insufficient_funds`), `pm_card_fail_then_ok` (falha na 1ª tentativa, sucesso depois).
- [ ] `PUT /v1/billing/payment-method` `{ token }` salva o meio de pagamento padrão (só o token + bandeira/últimos 4 simulados). Se houver fatura `Open` em `PastDue`/`Suspended`, dispara a retentativa imediata.
- [ ] `InvoiceIssued` com total > 0 e meio de pagamento → cria `charge` (tentativa 1, `idempotency_key = {invoiceId}:{attempt}`) e chama o gateway. Sem meio de pagamento → nenhuma cobrança; a fatura fica `Open`.
- [ ] `POST /webhooks/payments/{provider}`:
  - valida `X-Subscribely-Signature: t={unix},v1={hex}` = HMAC(secret, `{t}.{body}`) com comparação em tempo constante e tolerância de 5 min → senão, 401 sem detalhes;
  - grava `payment_events` (UNIQUE `provider, provider_event_id`), enfileira o processamento e responde **200 em < 200 ms**;
  - evento repetido → 200 sem novo processamento.
- [ ] Processamento: `charge.succeeded` → charge `Succeeded` + `IBillingApi.MarkInvoicePaid`; `charge.failed` → charge `Failed` + agenda o dunning.
- [ ] Ordem: evento para uma charge já em estado terminal é marcado `ignored` (ex.: `failed` após `succeeded`).
- [ ] Valor do evento ≠ valor da charge → evento `rejected` + log de alerta; a fatura não muda.
- [ ] **Dunning:** a 1ª falha publica `PaymentFailed` (assinatura → `PastDue`); retentativas em D+1, D+3 e D+7 a partir da emissão (`DunningJob` de hora em hora); a falha na D+7 publica `PaymentRetriesExhausted` (→ `Suspended`).
- [ ] `POST /v1/invoices/{id}/pay` (TenantAdmin) força uma nova tentativa com o meio atual; 409 se já houver charge `Pending`.
- [ ] Chamadas ao gateway via `HttpClient` tipado com o resilience handler padrão (timeout, retry com jitter, circuit breaker), sempre reutilizando a mesma `idempotency_key` no retry.
- [ ] Endpoint de desenvolvimento `POST /dev/fake-gateway/events/{eventId}/replay` reenvia um webhook (só em Development) para demonstrar a idempotência.

## Out of scope (explicit)

- Gateways reais (Stripe etc.), reembolsos, chargebacks, PIX/boleto, armazenar dados de cartão (PCI).

## Data contracts

- **Webhook body:** `{ id: "evt_*", type: "charge.succeeded"|"charge.failed", createdAt, data: { chargeId: "ch_*", invoiceId: uuid, amountCents: long, failureCode?: string } }`
- **Catálogo `payments`:**
  - `payment_methods(id, tenant_id UNIQUE, gateway_token, brand, last4, updated_at)`
  - `charges(id uuid, invoice_id, attempt int, gateway_charge_id NULL, idempotency_key UNIQUE, amount_cents, status[Pending|Succeeded|Failed], failure_code NULL, created_at, completed_at NULL)`
  - `payment_events(id, provider, provider_event_id, type, payload jsonb, status[Received|Processed|Ignored|Rejected], received_at, processed_at NULL, UNIQUE(provider, provider_event_id))`
  - `dunning(invoice_id PK, attempts int, next_attempt_at NULL, exhausted_at NULL)`
- **Eventos:** `PaymentSucceeded`, `PaymentFailed { invoiceId, attempt, failureCode, nextAttemptAt? }`, `PaymentRetriesExhausted`.
- **External dependencies:** gateway de pagamento (Fake na v1).

## Authorization

- Webhook: anônimo + assinatura HMAC (sem JWT). `/dev/*`: só em Development. Meio de pagamento e pay: TenantAdmin.

## Edge cases and failure modes

| Caso | Comportamento esperado |
|---|---|
| Timestamp da assinatura > 5 min | 401 (anti-replay) |
| Body alterado | 401 |
| Gateway fora do ar ao criar a charge | Retry do resilience handler; se esgotar, a charge fica `Failed` com `gateway_unavailable` e entra no dunning |
| Webhook chega antes do commit da charge | Processamento reenfileirado com backoff (até 5×) até a charge existir |
| Fatura anulada com charge pendente | Sucesso posterior → evento `ignored` + alerta de reconciliação |
| Dois webhooks iguais simultâneos | O UNIQUE garante 1 inserção; o outro recebe 200 |

## Decisions taken

| Decisão | Alternativa rejeitada | Por quê |
|---|---|---|
| Aceitar rápido e processar via job | Processar no request | O gateway exige resposta rápida; retries isolados |
| Fake por HTTP real | Fake chamando o handler direto | Exercita assinatura, rede e idempotência de verdade |
| Chave de idempotência por tentativa | Por fatura | Permite novas tentativas legítimas e torna o retry técnico seguro |

## Constraints

- Segredo HMAC por env/user-secrets; nunca logar o payload com dados sensíveis (não há cartão).

## Open questions (risks)

- Nenhuma. Resolvida: ao esgotar o dunning, a fatura continua `Open` (o pagamento manual reativa a assinatura) até ser paga ou anulada após 30 dias de suspensão (spec 03).
