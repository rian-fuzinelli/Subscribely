---
name: subscribely-background-jobs
description: Processamento em background do Subscribely com Hangfire (storage PostgreSQL) — jobs recorrentes de trial, fim de período, dunning, snapshot de uso, processador de outbox, idempotência de jobs, lotes, locks e testes determinísticos com FakeTimeProvider. Use ao criar, agendar ou testar qualquer job ou consumidor assíncrono.
---

# Jobs em background — Subscribely

## Infra
- Hangfire com `Hangfire.PostgreSql` no schema `hangfire` do Catálogo. Dashboard em `/hangfire`, só para PlatformAdmin.
- Jobs recorrentes registrados no startup por `RecurringJobs.Register(IRecurringJobManager)` de cada módulo (o registro remove os ids obsoletos).
- Filas: `critical` (webhooks, cobranças), `default`, `low` (snapshots, e-mails).

## Catálogo de jobs
| Job | Agenda | Módulo |
|---|---|---|
| `OutboxProcessorJob` | a cada 10 s | Infrastructure |
| `ProvisionTenantJob` | fire-and-forget (3 tentativas) | Tenancy |
| `TrialEndingNotifierJob` | diário 09:00 UTC | Billing |
| `TrialExpiryJob`, `PeriodEndJob` | de hora em hora | Billing |
| `ProcessPaymentEventJob` | fire-and-forget (fila critical) | Payments |
| `DunningJob` | de hora em hora | Payments |
| `UsageSnapshotJob` | de hora em hora | Metering |

## Regras de ouro
1. **Idempotente sempre:** o Hangfire é at-least-once. Rodar 2× = no-op (cheque o estado atual, use chaves UNIQUE).
2. **Argumentos pequenos e serializáveis:** passe ids, nunca entidades nem `DbContext`.
3. **Lotes de 100** com `ORDER BY id` + cursor; uma transação por item (a falha de um não derruba o lote).
4. **Concorrência:** `[DisableConcurrentExecution(timeoutInSeconds: 300)]` em jobs recorrentes; concorrência otimista nas entidades.
5. **Tempo:** sempre `TimeProvider`; o "agora" é lido uma vez no início do job.
6. `CancellationToken` vindo do Hangfire (`IJobCancellationToken`/`CancellationToken`) propagado.
7. Retentativas: `[AutomaticRetry(Attempts = N)]` explícito; erro de negócio esperado **não** lança exceção (retorna e loga).

## Outbox
- `outbox.messages` gravada no mesmo `SaveChanges` do agregado. O processador lê `processed_at IS NULL AND attempts < 5` com `FOR UPDATE SKIP LOCKED`, despacha para `IIntegrationEventHandler<T>` e marca `processed_at`. Erro → `attempts++`, `last_error`.

## Testes
- Em teste, os jobs rodam **síncronos** (`SyncJobClient`) e o tempo avança com `FakeTimeProvider.Advance(...)`.
- Todo job tem um teste de "execução dupla = mesmo resultado".

## Skills relacionadas
`dotnet-claude-kit:messaging` (padrões de outbox), `backend-design:idempotency-and-side-effects`.
