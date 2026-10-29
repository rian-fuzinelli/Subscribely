# Plan — 09 E2E & Quality

## Branch and delivery

- **Branch:** `test/e2e-scenarios` (base: `develop`)
- **PR slicing:** 2 PRs: (1) fixture + cenários 1–4; (2) cenários 5–10 + cobertura + CI final.

> A fixture (T01) é antecipada para a onda 1: todas as specs a usam. Os cenários só rodam quando as specs dependentes estiverem mergeadas.

## Technical approach

`SubscribelyApiFactory` (WebApplicationFactory) substitui `TimeProvider` por `FakeTimeProvider`,
o storage do Hangfire por execução síncrona (`IBackgroundJobClient` de teste) e aponta as conexões
para os containers. Helpers fluentes (`await Given.TenantSignedUp("pro")...`) deixam os cenários
legíveis. `MailpitClient` consulta a API HTTP do Mailpit.

## Affected files (by layer)

| Camada | Arquivo | Mudança |
|---|---|---|
| Fixture | `tests/Subscribely.IntegrationTests/Infrastructure/{SubscribelyApiFactory.cs, ContainersFixture.cs, SyncJobClient.cs, MailpitClient.cs}` | Base |
| DSL | `.../Infrastructure/Scenario/{Given.cs, When.cs, ThenExtensions.cs}` | Legibilidade |
| Cenários | `.../E2E/{HappyPath, Upgrade, Downgrade, Cancellation, Dunning, WebhookIdempotency, TenantIsolation, Quota, LongSuspension, PasswordReset}Tests.cs` | 10 cenários |
| CI | `.github/workflows/ci.yml`, `coverlet.runsettings` | Pipeline |

## Risks and mitigation

| Risco | Mitigação |
|---|---|
| Testes lentos | Containers por coleção + `Respawn` para limpar o Catálogo entre testes |
| Flakiness de webhooks HTTP | O FakeGateway em teste envia via `HttpClient` do próprio factory (in-memory) |

## Rollback and reversibility

- Só testes e CI; reverter basta.

## Testing strategy

- Esta spec **é** a estratégia: os 10 cenários + cobertura + smoke.
