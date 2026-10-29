---
name: subscribely-testing
description: Estratégia de testes do Subscribely — unitários (xUnit v3), integração com WebApplicationFactory + Testcontainers (Postgres, Redis, Mailpit), cenários E2E por HTTP, FakeTimeProvider, jobs síncronos, testes de isolamento entre tenants, de idempotência e de arquitetura, e metas de cobertura. Use ao escrever ou corrigir qualquer teste.
---

# Testes — Subscribely

## Projetos
- `tests/Subscribely.UnitTests` — domínio puro e funções (pastas por módulo). Sem I/O.
- `tests/Subscribely.IntegrationTests` — `SubscribelyApiFactory` + `ContainersFixture` (Postgres, Redis, Mailpit) por coleção; `Respawn` limpa o Catálogo entre testes.
- `tests/Subscribely.ArchitectureTests` — NetArchTest: fronteiras de módulo, tipos `internal`, BuildingBlocks sem referência a módulos.

## Ferramentas
xUnit v3 · Shouldly (assertions) · `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) · Testcontainers (PostgreSql, Redis, módulo genérico para o Mailpit) · Respawn · Coverlet.

## Regras
- Nome: `Metodo_Cenario_ResultadoEsperado` (ex.: `ChangePlan_DowngradeWithExcessSeats_Returns409`).
- AAA (Arrange/Act/Assert) visível; um comportamento por teste; tabela (`[Theory]`) para regras numéricas.
- **Nada de tempo real:** `FakeTimeProvider.Advance(TimeSpan.FromDays(14))`. Nada de `Task.Delay` esperando um job: use `SyncJobClient`.
- Cada teste cria os próprios tenants com slug aleatório; nunca depende da ordem de execução.
- Teste de integração usa a API HTTP pública (`HttpClient` do factory), não chama handlers internos.
- Para cada endpoint novo: caso feliz, validação (400), autorização (401/403) e **isolamento A×B (404)**.
- Para cada job/consumidor: **execução dupla = mesmo estado**.
- Para cada webhook: válido, assinatura inválida, duplicado, fora de ordem.

## Metas de cobertura
- ≥ 90% em Billing e Payments; 100% em `ProrationCalculator`, `Subscription` (máquina de estados) e `WebhookSignatureVerifier`.
- Exceções justificadas no PR (Notable Decisions).

## DSL dos cenários E2E
```csharp
var tenant = await Given.TenantSignedUp(plan: "pro", interval: Monthly);
await tenant.SetPaymentMethod("pm_card_declined");
await Time.Advance(days: 14);
(await tenant.Subscription()).Status.ShouldBe("PastDue");
```

## Comandos
```bash
dotnet test tests/Subscribely.UnitTests
dotnet test tests/Subscribely.IntegrationTests   # requer Docker
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```

## Skills relacionadas
`dotnet-claude-kit:testing`, `dotnet-claude-kit:tdd`, `backend-design:testing-with-discernment`.
