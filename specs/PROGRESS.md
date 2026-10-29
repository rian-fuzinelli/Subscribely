# Progresso — execução autônoma

> **Como retomar (nova sessão):** leia este arquivo → abra o `tasks.md` da spec ativa →
> `git switch <branch ativa>` → continue a primeira tarefa `[ ]`. Não pergunte nada ao humano:
> decida pela opção mais simples coerente com a spec e registre a decisão abaixo.

## Estado atual

- **Spec ativa:** `08-audit-trail`
- **Branch ativa:** `feat/audit-trail`
- **Próxima tarefa:** ver `specs/08-audit-trail/tasks.md`

## Ordem de execução

00 → 02 → 08 → 01 → 03 → 07 → 06 → 04 → 05 → 09 (sequencial nesta sessão; 02 antes de 01 porque o signup valida o plano).

## Desvios do fluxo padrão (decisões autônomas)

| Data | Decisão | Motivo |
|---|---|---|
| 2026-10-28 | Sem `origin` e sem `gh`: a fase *close* faz `git merge --no-ff` da branch em `develop` localmente e salva o rascunho do PR em `docs/prs/<branch>.md` | Não há repositório remoto configurado; o humano ordenou autonomia total |
| 2026-10-28 | Os agentes customizados (`.claude/agents`) só carregam em uma sessão nova; nesta sessão o orquestrador implementa diretamente, seguindo as mesmas skills | Os subagent types não estavam disponíveis na sessão em que foram criados |
| 2026-10-28 | O git usa `core.autocrlf=false` neste repositório | Evita diffs de fim de linha em massa |
| 2026-10-28 | Outbox despachado por `BackgroundService` (polling de 2 s) em vez de job recorrente do Hangfire | Cron do Hangfire tem granularidade mínima de 1 min |
| 2026-10-28 | Validação por filtro DataAnnotations (`WithRequestValidation<T>()`) em vez de `AddValidation()` | O gerador do .NET 10 não cobre endpoints em bibliotecas de módulo |
| 2026-10-28 | Validação de JWT e políticas RBAC ficam em `Infrastructure/Security`; a emissão de tokens fica no módulo Tenancy | Todos os módulos precisam das políticas antes da spec 01 |
| 2026-10-28 | Specs executadas em sequência pelo orquestrador (sem subagentes paralelos) | Os agentes customizados não estão carregados nesta sessão; a sequência evita conflitos de merge |

## Specs concluídas

| Spec | Branch | Merge em develop | Testes |
|---|---|---|---|
| 00-foundation | `chore/foundation-modular-monolith` | 2026-10-28 | unit 39 · arch 15 · integration 13 · smoke do compose ok |
| 02-plans | `feat/billing-plans` | 2026-10-28 | unit 47 · integration 22 (3 execuções verdes) |

## Pendências para o humano (não bloqueiam)

- Criar o repositório no GitHub, adicionar `origin` e publicar `main`/`develop` + abrir os PRs a partir de `docs/prs/`.
