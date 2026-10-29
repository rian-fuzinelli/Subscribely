---
name: subscribely-architecture
description: Arquitetura de software do Subscribely (Modular Monolith + Vertical Slice, fronteiras entre módulos, control plane vs data plane, outbox, escrita de plan.md/tasks.md no padrão SDD). Use ao planejar uma spec, ao decompor tarefas para agentes paralelos ou ao criar peças transversais.
---

# Arquitetura — Subscribely

## Modelo mental
- **Control plane** (banco Catálogo): Tenancy, Billing, Payments, Metering (histórico), Notifications, Audit, Outbox, Hangfire. Um schema por módulo.
- **Data plane** (um banco por tenant, `subscribely_t_{slug}`): Projects (produto) + auditoria de produto.
- Os módulos se comunicam por `Contracts` (síncrono, in-process) ou por **eventos de integração no outbox** (assíncrono, at-least-once → consumidores idempotentes).

## Regras de fronteira (verificadas em `tests/Subscribely.ArchitectureTests`)
- O módulo X não referencia tipos de Y fora de `Subscribely.Modules.Y.Contracts`.
- BuildingBlocks não referenciam módulos. O Api referencia só `Add<M>Module()`.
- Um DbContext por módulo; nunca fazer join entre schemas de módulos diferentes. Precisa de dado alheio? Use o `Contracts`.

## Ao escrever `plan.md`
- Siga `specs/_template/plan.md`. Tabela "Affected files" com caminhos reais do layout.
- Aponte riscos de concorrência, idempotência e isolamento de tenant **sempre**.
- Prefira a solução mais simples que atende aos critérios de aceite; justifique cada nova dependência.

## Ao escrever `tasks.md` (para paralelismo)
- Formato: `- [ ] T## [P] @agente Descrição — done when: <critério verificável> (deps: T..)`.
- Tarefa ≤ ~1 h, com **um dono**, tocando de preferência um único slice/pasta.
- Marque `[P]` só se não editar os mesmos arquivos de outra `[P]` simultânea.
- Hotspots (`Program.cs`, `Directory.Packages.props`, migrations) ficam com o orquestrador.
- Ordem típica: persistência → domínio puro (testável) → slices → jobs/consumidores → testes de isolamento.
- Escolha o agente pela skill: domínio de billing → `billing-domain-engineer`; HMAC/JWT/RBAC → `security-engineer`; Hangfire → `background-jobs-engineer`; etc. (ver `ORCHESTRATION.md`).

## Peças transversais
- Outbox: `IOutbox.Add(evt)` dentro do mesmo `SaveChanges`; o processador (Hangfire) despacha para `IIntegrationEventHandler<T>`.
- Auditoria: interceptor do EF + `IAuditLog` para ações explícitas.
- Tenancy: `ITenantContext` (scoped) é a única fonte do tenant atual; nunca leia o `tid` do request por conta própria.

## Skills relacionadas
Para aprofundar, invoque: `dotnet-claude-kit:vertical-slice`, `dotnet-claude-kit:project-structure`, `backend-design:design`.
