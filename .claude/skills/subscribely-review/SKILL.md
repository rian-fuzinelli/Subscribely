---
name: subscribely-review
description: Revisão de código do Subscribely antes do merge de uma tarefa — aderência à spec/plan, regras de domínio, isolamento entre tenants, segurança, idempotência, testes e convenções, com veredito APROVADO/REPROVADO e apontamentos acionáveis. Use ao revisar o diff de uma branch task/* contra a feature branch.
---

# Revisão de tarefa — Subscribely

## Entrada
O orquestrador informa a spec, o id da tarefa e as branches. Obtenha o diff com:
```bash
git diff feat/<spec>...task/<spec>-T<NN>
git log --oneline feat/<spec>..task/<spec>-T<NN>
```
Leia `specs/<spec>/spec.md`, `plan.md` e a linha da tarefa em `tasks.md`.

## Checklist (em ordem de gravidade)
1. **Spec:** o critério "done when" foi atendido? Algo foi feito **fora** do escopo da tarefa/spec?
2. **Isolamento de tenant:** toda query do Catálogo é filtrada pelo `ITenantContext`? Existe teste A×B (404)?
3. **Segurança:** política de autorização explícita; nada sensível em log/resposta/auditoria; comparações em tempo constante (checklist da skill `subscribely-security`).
4. **Dinheiro e tempo:** `long` em centavos; `TimeProvider`; nenhum `DateTime.UtcNow`.
5. **Idempotência e concorrência:** jobs, consumidores e webhooks idempotentes; UNIQUE no banco; concorrência otimista quando houver disputa.
6. **Fronteiras:** nenhum acesso a tipos `internal` de outro módulo; nada fora de `Contracts`.
7. **Testes:** cobrem o caso feliz, validação, autorização, isolamento e o caso de borda da spec; nada de tempo real ou ordem implícita.
8. **Convenções:** 1 commit Conventional em inglês; sem migration gerada pelo implementer; sem edição de hotspots.
9. **Simplicidade:** abstrações sem uso, código morto, duplicação (KISS/YAGNI/DRY).

## Verificação
Rode, se o diff mexer em código: `dotnet build` e os testes afetados (`dotnet test --filter`). Reporte o resultado.

## Saída (obrigatória, nesta forma)
```
VEREDITO: APROVADO | REPROVADO
Bloqueadores:
- [arquivo:linha] problema → correção esperada
Sugestões (não bloqueiam):
- ...
Verificação executada: <comandos + resultado>
```
Reprove somente por bloqueadores (itens 1–8). Seja específico: o implementer recebe sua lista **literalmente**.
Você não edita código.

## Skills relacionadas
`dotnet-claude-kit:code-review`, `backend-design:audit`, `security-review`.
