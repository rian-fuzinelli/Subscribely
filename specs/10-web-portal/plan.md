# Plan — 10 Portal web

## Branch and delivery

- **Branch:** `feat/web-portal` (base: `develop`)
- **PR slicing:** PR único (API: 2 ajustes pequenos; web: app nova).

## Technical approach

- **App:** `web/` com Next.js 16 (App Router) + React 19 + TypeScript + Tailwind CSS 4, porta 3000
  (= `Notifications:PortalBaseUrl`, então os links dos e-mails já funcionam).
- **BFF (FR-007):** o navegador nunca vê o JWT. Server Actions e Server Components chamam a API pelo
  servidor do Next (`SUBSCRIBELY_API_URL`); access e refresh token ficam em cookies `HttpOnly`,
  `SameSite=Lax`, `Secure` fora de dev. O `proxy.ts` renova o access token pelo refresh antes de expirar.
- **Vertical Slice Architecture:** `web/src/features/<slice>/` (pricing, signup, auth, subscription,
  billing, invoices, usage, team, api-keys, audit, playground), cada slice com `schema.ts` (Zod),
  `api.ts` (chamadas server-only), `actions.ts` (Server Actions) e `components/`. `web/src/app/` só
  compõe rotas; `web/src/shared/` tem o cliente HTTP, sessão, formatação pt-BR e os primitivos de UI.
- **UI/UX:** primitivos próprios em Tailwind (botão, campo, card, badge, alerta, tabela, dialog),
  foco visível, contraste AA, estados vazio/carregando/erro, layout responsivo a partir de 360 px,
  feedback otimista só onde a API é síncrona; textos pt-BR, `R$ 1.234,56`, fuso de São Paulo.

## Affected files (by layer)

| Camada | Arquivo | Mudança |
|---|---|---|
| API | `Infrastructure/Security/AuthenticationSetup.cs` | Política `ProductAccess` (API key **ou** JWT de TenantAdmin) |
| API | `Modules.Projects/Features/ProjectEndpoints.cs` | Usa `ProductAccess` (playground) |
| API | `Modules.Tenancy/Features/Signup/SignupEndpoints.cs` | `GET /v1/signup/{tenantId}` (status do provisionamento, anônimo) |
| Web | `web/**` | App nova |
| Infra | `docker-compose.yml`, `.github/workflows/ci.yml` | Serviço `web`; job de build/teste do portal |
| Specs | `specs/06-metering/spec.md` | Registrar a nova regra de acesso ao produto |

## Risks and mitigation

| Risco | Mitigação |
|---|---|
| Token vazando para o browser | Só cookies HttpOnly; módulos `server-only` para tudo que lê o token |
| Sessão expirando no meio do uso | Renovação no `proxy.ts` + retorno à página pedida após login |
| Playground abrindo o produto para Members | Política exige papel TenantAdmin no JWT |

## Testing strategy

- API: testes de integração para a política nova (TenantAdmin passa e consome quota; Member → 403) e para o status do signup.
- Web: Vitest para formatação, mapeamento de erros da API (ProblemDetails → mensagens) e schemas; `next build` + typecheck + lint na CI.
