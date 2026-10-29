# Plan — 03 Subscriptions

## Branch and delivery

- **Branch:** `feat/billing-subscriptions` (base: `develop`)
- **PR slicing:** 2 PRs: (1) agregado + trial + endpoints de leitura/cancelamento; (2) change-plan + jobs de período + reativação.

## Technical approach

Agregado `Subscription` no módulo Billing concentra **todas** as transições em métodos (`StartTrial`,
`ConvertTrial`, `ChangePlan`, `Cancel`, `Resume`, `ClosePeriod`, `MarkPastDue`, `Suspend`,
`Reactivate`), cada um devolvendo `Result` e registrando eventos de domínio que viram outbox no
`SaveChanges`. A classificação upgrade/downgrade fica num serviço puro `PlanChangeClassifier`.
Jobs Hangfire (`TrialEndingNotifierJob` e `SuspensionExpiryJob` diários, `TrialExpiryJob` e `PeriodEndJob` de hora em hora)
processam em lotes de 100 com lock por assinatura.

## Affected files (by layer)

| Camada | Arquivo | Mudança |
|---|---|---|
| Domínio | `src/Modules/Subscribely.Modules.Billing/Domain/Subscriptions/{Subscription.cs, SubscriptionStatus.cs, PlanChangeClassifier.cs, BillingPeriod.cs}` | Agregado e regras |
| Endpoints | `.../Features/Subscriptions/{GetSubscription,ChangePlan,CancelPendingChange,Cancel,Resume,Reactivate}/*` | Slices |
| Consumidores | `.../Features/Subscriptions/OnTenantProvisioned.cs`, `OnInvoicePaid.cs`, `OnPaymentRetriesExhausted.cs` | Handlers de outbox |
| Jobs | `.../Features/Subscriptions/Jobs/{TrialEndingNotifierJob, TrialExpiryJob, PeriodEndJob, SuspensionExpiryJob}.cs` | Recorrentes |
| Persistência | `.../Persistence/Configurations/SubscriptionConfiguration.cs` | `xmin` como token de concorrência |

## Risks and mitigation

| Risco | Mitigação |
|---|---|
| Job e request alterando a mesma assinatura | Concorrência otimista + retry do job |
| Datas de fim de mês | `BillingPeriod.Next()` ancorado no dia original, coberto por testes de tabela |
| Evento processado 2× | Handlers idempotentes (checam o estado atual antes de transicionar) |

## Rollback and reversibility

- Migration aditiva. Reverter o PR basta; os jobs são removidos do registro recorrente na subida.

## Testing strategy

- Unit (100% da máquina de estados): todas as transições válidas e inválidas; `PlanChangeClassifier`; `BillingPeriod`.
- Integration: trial → conversão; trial → expiração; upgrade imediato; downgrade agendado aplicado pelo job (`FakeTimeProvider`); cancelar/retomar; concorrência 409.
