# Orquestração de agentes — Subscribely

> Como as specs viram código com vários subagentes trabalhando em paralelo numa única janela
> de contexto. A sessão principal do Claude Code é **sempre** a orquestradora: subagentes não
> criam outros subagentes.

## Princípios

1. **SDD primeiro:** nenhum agente codifica sem `spec.md` e `plan.md` aprovados por um humano.
2. **Especialista por tema:** cada agente tem uma skill própria (`.claude/skills/subscribely-*`) com as regras do seu domínio.
3. **Paralelo isolado:** cada implementer roda numa **git worktree** própria (`isolation: worktree`), numa branch `task/<spec>-<id>`.
4. **Contexto enxuto:** a sessão principal só planeja, despacha, integra e decide. Ela não implementa.

## Elenco

| Agente | Modelo | Skill própria | Responsabilidade |
|---|---|---|---|
| `software-architect` | opus | `subscribely-architecture` | Escreve `plan.md`/`tasks.md`, define fronteiras, ADRs, testes de arquitetura |
| `database-engineer` | sonnet | `subscribely-database` | EF Core, Catálogo/TenantDb, schemas, índices, provisionamento de bancos, RLS/permissões |
| `security-engineer` | sonnet | `subscribely-security` | JWT/refresh, RBAC, API keys, hashing, HMAC, segredos, rate limit de login |
| `billing-domain-engineer` | sonnet | `subscribely-billing-domain` | Planos, máquina de estados da assinatura, ciclos, faturas, proração |
| `payments-integration-engineer` | sonnet | `subscribely-payments-integration` | Gateway, serviços externos, webhooks idempotentes, dunning, resiliência HTTP |
| `metering-engineer` | sonnet | `subscribely-metering` | Quota em Redis, middleware de enforcement, seats, alertas de uso |
| `background-jobs-engineer` | sonnet | `subscribely-background-jobs` | Hangfire: jobs recorrentes, retries, idempotência de jobs, outbox processor |
| `notifications-engineer` | sonnet | `subscribely-notifications` | Templates, SMTP/Mailpit, deduplicação de e-mails, consumidores do outbox |
| `test-engineer` | sonnet | `subscribely-testing` | Testes unitários, de integração (Testcontainers) e E2E |
| `devops-engineer` | sonnet | `subscribely-devops` | Dockerfile, compose, health checks, CI (GitHub Actions) |
| `code-reviewer` | opus | `subscribely-review` | Revisa a aderência à spec, a segurança e a qualidade antes do merge (somente leitura) |

Todos os agentes também carregam `subscribely-conventions` (padrões de código, layout, commits).

## Formato das tarefas (`tasks.md`)

```
- [ ] T03 [P] @database-engineer Criar BillingDbContext (schema billing) — done when: migration aplica no Testcontainers (deps: T01)
```

- `T##`: id estável dentro da spec.
- `[P]`: pode rodar em paralelo com outras `[P]` cujas `deps` já estejam concluídas.
- `@agente`: dono da tarefa.
- `deps`: ids que precisam estar `[x]` antes.

## Ciclo por onda

```
1. Humano aprova spec.md da(s) spec(s) da onda
2. software-architect escreve plan.md + tasks.md → humano aprova
3. Orquestrador cria a feature branch feat/<spec> a partir de develop   (dev-lifecycle: kickoff)
4. Orquestrador despacha as tarefas prontas ([P] + deps ok), até 3 implementers simultâneos,
   cada um com isolation: worktree e branch task/<spec>-T##
5. Implementer: código + testes → build e testes verdes → 1 commit (Conventional Commit, inglês)
6. code-reviewer revisa o diff contra spec.md/plan.md
     ├─ aprovado  → orquestrador mergeia task/* em feat/<spec>, gera a migration (se houver), marca [x]
     └─ reprovado → volta ao MESMO implementer (SendMessage) com os apontamentos; máx. 2 rodadas,
                    depois escala para o humano
7. Ao final da spec: test-engineer amplia a cobertura → dev-lifecycle close (CI → push → PR draft → pausa)
```

## Onde ficam os arquivos

| O quê | Onde | Versionado? |
|---|---|---|
| Definição dos agentes (modelo, ferramentas, skills pré-carregadas) | `.claude/agents/*.md` | Não, por enquanto (`.gitignore`) |
| Skill de cada especialidade | `.claude/skills/subscribely-*/SKILL.md` | Sim |
| Specs, plans e tasks | `specs/<NN-spec>/` | Sim |

**Worktrees:** os implementers têm `isolation: worktree` no frontmatter. A worktree parte da branch
**padrão** (`main`), não da feature branch, por isso a primeira ação de todo implementer é
`git switch -c task/<spec>-T## feat/<spec>` (regra da skill `subscribely-conventions`).
`software-architect` e `code-reviewer` trabalham na árvore principal: o arquiteto escreve os
documentos e, para tarefas de código, o orquestrador o despacha com `isolation: "worktree"`.

## Hotspots de merge

- Cada módulo se registra via `Add<X>Module()` no próprio projeto. `Program.cs` só ganha uma linha por módulo, editada pelo orquestrador.
- Configuração de entidades via `IEntityTypeConfiguration<T>` dentro do módulo.
- **Implementers não geram migrations.** O orquestrador gera uma por merge que altera o modelo (`dotnet ef migrations add`).
- `Directory.Packages.props` (versões centralizadas) é editado só pelo orquestrador; implementers pedem o pacote no relatório.

## Relatório obrigatório do implementer (ao terminar)

1. Tarefas concluídas (ids) e critério de pronto atendido.
2. Arquivos alterados.
3. Comandos de verificação executados e resultado (build/testes).
4. Pacotes NuGet ou mudanças de modelo que exigem ação do orquestrador.
5. Desvios da spec (devem ser zero; se houver, justificar).

## Decisões confirmadas

- Até **3 implementers** simultâneos por onda.
- Repositório remoto e CI: **GitHub + GitHub Actions** (PRs via `gh`).
- A branch `develop` ainda não existe; ela é criada na tarefa T01 da spec 00, junto com o commit inicial.
