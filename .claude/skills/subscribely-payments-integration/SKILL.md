---
name: subscribely-payments-integration
description: Integração de pagamentos e serviços externos do Subscribely — abstração IPaymentGateway, FakePaymentGateway com webhooks HTTP assinados, recepção de webhooks insert-first idempotente e tolerante a ordem, chaves de idempotência, dunning e HttpClient resiliente. Use em gateways, webhooks, cobranças e qualquer chamada a serviço externo.
---

# Pagamentos, gateways e webhooks — Subscribely

## Gateway
- Interface `IPaymentGateway.CreateChargeAsync(invoiceId, amountCents, paymentMethodToken, idempotencyKey, ct)` → `{ gatewayChargeId, status: Pending }`.
- Provider escolhido por `Payments:Provider` (`Fake` | `None`). Nada no domínio conhece o provider concreto.
- **FakePaymentGateway**: respeita a idempotência (mesma chave → mesma charge); decide o resultado pelo token:
  `pm_card_ok` ✓ · `pm_card_declined` ✗ `card_declined` · `pm_card_insufficient_funds` ✗ · `pm_card_fail_then_ok` ✗ na 1ª tentativa, ✓ depois.
  Agenda (Hangfire, 1–3 s) o envio **HTTP real** do webhook assinado para `Payments:WebhookBaseUrl`.

## Chamadas a serviços externos
- Sempre por `HttpClient` tipado (`AddHttpClient<T>`) + `AddStandardResilienceHandler()` (timeout, retry com jitter, circuit breaker).
- Retry técnico **reutiliza a mesma idempotency key**. A chave é `{invoiceId}:{attempt}`.
- Timeout total ≤ 10 s; falha definitiva → charge `Failed` com `gateway_unavailable` → entra no dunning.

## Webhooks (recepção)
1. Rota `POST /webhooks/payments/{provider}`, anônima, com `EnableBuffering`.
2. Verificar a assinatura (skill `subscribely-security`). Falha → 401.
3. **Insert-first** em `payment_events` (UNIQUE `provider, provider_event_id`). Conflito → 200 imediato (duplicado).
4. Enfileirar `ProcessPaymentEventJob(eventId)` e responder **200** (< 200 ms).
5. No job: carregar a charge; se não existir ainda → reenfileirar com backoff (máx. 5); se estiver em estado terminal → evento `Ignored`; valor divergente → `Rejected` + log de alerta; senão, aplicar a transição e marcar `Processed`, **na mesma transação**.

## Dunning
- 1ª falha → `PaymentFailed` (assinatura → PastDue) e `next_attempt_at = issued_at + 1d`.
- Agenda: D+1, D+3, D+7 a partir da emissão. `DunningJob` de hora em hora pega `next_attempt_at <= now`.
- Falha na D+7 → `PaymentRetriesExhausted`. Pagamento manual ou novo meio de pagamento → tentativa imediata.
- Uma charge `Pending` por fatura no máximo (senão, 409).

## Proibido
- Armazenar número de cartão/CVV (só tokens).
- Processar o webhook antes de persisti-lo.
- Confiar no valor do webhook sem comparar com a charge.

## Skills relacionadas
`dotnet-claude-kit:httpclient-factory`, `dotnet-claude-kit:resilience`, `backend-design:idempotency-and-side-effects`, `backend-design:error-handling-as-design`.
