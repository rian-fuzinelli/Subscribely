# Spec — 09 E2E & Quality (cenários ponta a ponta e CI)

> Status: **APROVADO** — execução autônoma autorizada pelo humano em 2026-10-28. Herda as decisões de `specs/OVERVIEW.md`.

## Context and problem

Cada módulo tem os próprios testes, mas o valor do produto está nos **fluxos entre módulos**
(signup → trial → cobrança → webhook → e-mail → quota). Esta spec define os cenários que provam
o critério de sucesso global e a pipeline que os mantém verdes.

## Acceptance criteria (verifiable)

- [ ] Projeto `tests/Subscribely.IntegrationTests` com fixture compartilhada: `WebApplicationFactory` + Testcontainers (Postgres, Redis, Mailpit) + `FakeTimeProvider` controlável + execução síncrona de jobs Hangfire em teste.
- [ ] Cenários E2E (um teste cada, usando só a API HTTP):
  1. **Feliz:** signup → `Active` → API key → 10 chamadas em `/v1/projects` → fim do trial com `pm_card_ok` → fatura `Paid` → e-mails `trial-ending`, `invoice-issued` e `invoice-paid` no Mailpit.
  2. **Upgrade:** no meio do período → fatura `Upgrade` com proração correta → nova quota vale na hora.
  3. **Downgrade:** agendado → bloqueado por seats (409) → remove usuário → agenda → aplicado no fim do período.
  4. **Cancelamento:** cancelar → retomar → cancelar → fim do período → `Canceled` → produto 402.
  5. **Dunning:** `pm_card_declined` → `PastDue` → D+1/D+3/D+7 → `Suspended` → troca para `pm_card_ok` → `Active`.
  6. **Idempotência:** replay do mesmo webhook 3× → uma charge `Succeeded`, uma fatura `Paid`, um e-mail.
  7. **Isolamento:** o tenant B não lê, altera nem lista nada do tenant A (JWT e API key) → 404.
  8. **Quota:** limite atingido → 429 com headers; e-mails de 80% e 100% enviados uma vez, só para os TenantAdmins.
  9. **Suspensão longa:** `Suspended` + 30 dias → `Canceled`, fatura `Void`; reativar com `pm_card_ok` → `Active` e os projetos do tenant continuam lá.
  10. **Senha:** pedido de reset → e-mail no Mailpit → confirmação → login com a nova senha; o refresh token antigo → 401.
- [ ] `tests/Subscribely.ArchitectureTests` verdes (fronteiras de módulo, tipos `internal`).
- [ ] Cobertura ≥ 90% nos módulos Billing e Payments; 100% em `ProrationCalculator`, máquina de estados da assinatura e `WebhookSignatureVerifier` (Coverlet).
- [ ] GitHub Actions: `restore → build → test (unit + arch + integration) → coverage report`, em PR e push para `develop`/`main`.
- [ ] Smoke do compose na CI: `docker compose up -d` → `/health/ready` = 200 em até 60 s.

## Out of scope (explicit)

- Testes de carga/performance com k6, testes de mutação, deploy.

## Data contracts

- Nenhum contrato novo; usa os contratos HTTP das specs 01–08.

## Authorization

- N/A (os testes exercitam as regras das outras specs).

## Edge cases and failure modes

| Caso | Comportamento esperado |
|---|---|
| Teste depende de tempo real | Proibido: tudo via `FakeTimeProvider` |
| Teste depende de ordem de execução | Cada teste cria os próprios tenants (slug aleatório) |
| Docker indisponível na máquina | Os testes de integração são pulados com mensagem clara; a CI sempre tem Docker |

## Decisions taken

| Decisão | Alternativa rejeitada | Por quê |
|---|---|---|
| Containers compartilhados por coleção | Um container por teste | Tempo de execução |
| Jobs executados de forma síncrona em teste | Esperar o Hangfire real | Determinismo |

## Constraints

- A suíte completa roda em < 10 min na CI.

## Open questions (risks)

- Nenhuma. Resolvida: repositório no **GitHub** com **GitHub Actions**. O MCP do GitHub falhou ao conectar na sessão de 28/10/2026; os PRs usam o `gh` CLI autenticado.
