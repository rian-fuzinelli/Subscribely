---
name: subscribely-conventions
description: Convenções obrigatórias do projeto Subscribely (layout Modular Monolith + Vertical Slice, estilo C#, git por tarefa, relatório ao orquestrador). Carregada por todos os agentes do projeto; use sempre antes de escrever ou revisar código no Subscribely.
---

# Convenções do Subscribely

## Fontes da verdade (ler antes de agir)
1. `specs/OVERVIEW.md` — decisões globais e layout.
2. `specs/ORCHESTRATION.md` — fluxo de agentes, formato de tarefas e hotspots.
3. `specs/<NN-spec>/spec.md`, `plan.md` e `tasks.md` da tarefa recebida.
Se o código pedido contradiz a spec, **pare e reporte**. Nunca "melhore" a spec por conta própria.

## Git por tarefa (você roda numa worktree que parte de `main`)
```bash
git fetch --all 2>/dev/null; git switch -c task/<spec>-T<NN> feat/<spec>   # primeira ação
# ... implementar ...
dotnet build && dotnet test                                                # gates
git add -A && git commit -m "<type>(<module>): <descrição em inglês>"      # 1 tarefa = 1 commit
```
- Conventional Commits em inglês: `feat`, `fix`, `refactor`, `test`, `chore`, `docs`. Escopo = módulo (`billing`, `tenancy`...).
- Nunca faça push, merge, rebase de branches alheias ou `--no-verify`.
- **Nunca gere migrations EF** nem edite `Directory.Packages.props` ou `Program.cs`: peça no relatório.

## Layout
- Código do módulo: `src/Modules/Subscribely.Modules.<M>/` → `Features/<Slice>/`, `Domain/`, `Persistence/`, `Contracts/`.
- Um slice = endpoint + request/response + handler no mesmo diretório. Sem camadas "Services/Repositories" genéricas.
- Tudo `internal`, exceto `Contracts/*` e `<M>Module.Add<M>Module()`. Outro módulo só via `Contracts` ou evento no outbox.

## Estilo C# (.NET 10 / C# 14)
- `sealed` por padrão; `record` para DTOs/eventos; primary constructors para DI.
- File-scoped namespaces; `var` quando o tipo é óbvio; nullable habilitado, sem `!` para silenciar.
- `CancellationToken` em toda API assíncrona, propagado até o EF/Redis/HTTP.
- Erros de negócio: `Result`/`Error` (SharedKernel) → ProblemDetails. Exceção só para o inesperado.
- Tempo: injete `TimeProvider`; proibido `DateTime.Now/UtcNow`. Dinheiro: `long` em centavos.
- Logs estruturados (`logger.LogInformation("Invoice {InvoiceId} paid", id)`); nunca logar segredos ou PII em claro.
- KISS, YAGNI, DRY e SoC: sem abstração sem um segundo uso real nesta spec.

## Relatório final obrigatório (sua última mensagem)
1. Tarefas (ids) concluídas e o critério "done when" atendido.
2. Arquivos alterados.
3. Comandos de verificação executados + resultado.
4. Pedidos ao orquestrador: pacotes NuGet, migrations, linhas em `Program.cs`.
5. Desvios da spec (esperado: nenhum).
