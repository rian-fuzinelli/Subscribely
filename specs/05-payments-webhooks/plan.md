# Plan — 05 Payments, Gateway & Webhooks

## Branch and delivery

- **Branch:** `feat/payments-webhooks` (base: `develop`)
- **PR slicing:** 2 PRs: (1) gateway + meio de pagamento + charges; (2) webhooks + dunning + pay manual.

## Technical approach

Módulo `Subscribely.Modules.Payments`. `IPaymentGateway` com `FakePaymentGateway`, que agenda via
Hangfire o envio do webhook assinado para a própria API. Recepção de webhook: middleware de
verificação HMAC (lendo o body bruto) → insert-first em `payment_events` → job de processamento.
Dunning por `DunningJob` recorrente. Comunicação com Billing só via `IBillingApi` e eventos do outbox.

## Affected files (by layer)

| Camada | Arquivo | Mudança |
|---|---|---|
| Gateway | `src/Modules/Subscribely.Modules.Payments/Gateway/{IPaymentGateway.cs, FakePaymentGateway.cs, FakeWebhookSender.cs, GatewayHttpClient.cs}` | Abstração + Fake |
| Webhooks | `.../Features/Webhooks/{ReceiveWebhookEndpoint.cs, WebhookSignatureVerifier.cs, ProcessPaymentEventJob.cs}` | Recepção e processamento |
| Cobrança | `.../Features/Charges/{OnInvoiceIssued.cs, ChargeInvoice.cs, PayInvoiceEndpoint.cs}` | Fluxo de cobrança |
| Dunning | `.../Features/Dunning/{DunningSchedule.cs, DunningJob.cs}` | Retentativas |
| Meio de pagamento | `.../Features/PaymentMethods/SetPaymentMethodEndpoint.cs` | |
| Dev | `.../Features/Dev/ReplayFakeEventEndpoint.cs` | Só em Development |
| Persistência | `.../Persistence/{PaymentsDbContext.cs, Configurations/*}` | Schema `payments` |

## Risks and mitigation

| Risco | Mitigação |
|---|---|
| Leitura do body para o HMAC consome o stream | `EnableBuffering` só na rota de webhook |
| Cobrança dupla | `idempotency_key` UNIQUE + o Fake respeita a chave (devolve a mesma charge) |
| Ordem de eventos | Máquina de estados da charge com estados terminais |

## Rollback and reversibility

- Migration aditiva. Desligar a cobrança: `Payments:Provider = None` (o registro não cobra).

## Testing strategy

- Unit: verificador de assinatura (válida, expirada, adulterada), `DunningSchedule`, transições de charge.
- Integration: fluxo `pm_card_ok`; `pm_card_declined` até `Suspended` com `FakeTimeProvider`; webhook duplicado (replay) = 1 efeito; webhook fora de ordem; valor divergente; pay manual.
