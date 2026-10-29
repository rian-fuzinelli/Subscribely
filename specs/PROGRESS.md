# Progresso — execução autônoma

> **Como retomar (nova sessão):** leia este arquivo → abra o `tasks.md` da spec ativa →
> `git switch <branch ativa>` → continue a primeira tarefa `[ ]`. Não pergunte nada ao humano:
> decida pela opção mais simples coerente com a spec e registre a decisão abaixo.

## Estado atual

- **Spec ativa:** `00-foundation`
- **Branch ativa:** `chore/foundation-modular-monolith`
- **Próxima tarefa:** ver `specs/00-foundation/tasks.md`

## Ordem de execução

00 → 01, 02, 08 → 03, 06, 07 → 04, 05 → 09 (ver `OVERVIEW.md`).

## Desvios do fluxo padrão (decisões autônomas)

| Data | Decisão | Motivo |
|---|---|---|
| 2026-10-28 | Sem `origin` e sem `gh`: a fase *close* faz `git merge --no-ff` da branch em `develop` localmente e salva o rascunho do PR em `docs/prs/<branch>.md` | Não há repositório remoto configurado; o humano ordenou autonomia total |
| 2026-10-28 | Os agentes customizados (`.claude/agents`) só carregam em uma sessão nova; nesta sessão o orquestrador implementa diretamente, seguindo as mesmas skills | Os subagent types não estavam disponíveis na sessão em que foram criados |
| 2026-10-28 | O git usa `core.autocrlf=false` neste repositório | Evita diffs de fim de linha em massa |

## Specs concluídas

_(nenhuma ainda)_

## Pendências para o humano (não bloqueiam)

- Criar o repositório no GitHub, adicionar `origin` e publicar `main`/`develop` + abrir os PRs a partir de `docs/prs/`.
