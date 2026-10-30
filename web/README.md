# Portal web do Subscribely

Interface dos clientes (visitante, TenantAdmin e Member) sobre a API — spec [`10-web-portal`](../specs/10-web-portal/spec.md).

## Stack

Next.js 16 (App Router, Server Components e Server Actions) · React 19 · TypeScript · Tailwind CSS 4 · Zod · Vitest.

## Arquitetura

- **BFF:** o navegador só fala com o servidor do Next. O JWT e o refresh token ficam em cookies `HttpOnly`;
  as chamadas à API (`SUBSCRIBELY_API_URL`) saem do servidor (`src/shared/api/client.ts`, `server-only`).
  O [`proxy.ts`](src/proxy.ts) renova o access token antes de expirar e manda para o login, com retorno, quem
  abre uma rota protegida sem sessão.
- **Vertical Slice Architecture** em `src/features/<slice>/`:

  | Arquivo | Papel |
  |---|---|
  | `api.ts` | Leituras da API (server-only), usadas pelos Server Components |
  | `actions.ts` | Server Actions (mutações), validadas com Zod |
  | `model.ts` / `schema.ts` | Tipos, regras de exibição e validação — puros e testados |
  | `components/` | UI do slice |
  | `index.ts` | O que o slice expõe; o ESLint proíbe importar o interior de outro slice |

  Slices: `pricing`, `signup`, `auth`, `subscription`, `payment-method`, `invoices`, `usage`, `team`,
  `api-keys`, `audit`, `playground`. `src/app/` só compõe as rotas; `src/shared/` tem o cliente HTTP, a
  sessão, a formatação pt-BR e os primitivos de UI.
- **UI/UX:** pt-BR, `R$ 1.234,56` e horário de São Paulo; foco visível, `aria-*` nos campos, diálogos nativos
  para confirmar ações destrutivas, estados de carregamento/erro/vazio, tabelas que viram lista no celular.

## Rodando

```bash
cp .env.example .env.local   # SUBSCRIBELY_API_URL (padrão http://localhost:5055)
npm install
npm run dev                  # http://localhost:3000
```

Pelo compose da raiz (`docker compose up -d --build`) o portal sobe em http://localhost:3000 apontando para a API do compose.

```bash
npm run lint && npm run typecheck && npm test && npm run build
```
