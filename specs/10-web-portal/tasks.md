# Tasks — 10 Portal web

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída.

**Branch:** `feat/web-portal` (base: `develop`)

## Tasks

- [x] T01 API: política `ProductAccess` + status público do signup — done when: testes de integração verdes
- [x] T02 Scaffold `web/` (Next 16, TS, Tailwind 4, Zod, Vitest, ESLint) + shared (cliente HTTP, sessão, formatação, UI) — done when: build e testes verdes
- [x] T03 Slices pricing + signup (com acompanhamento do provisionamento) — done when: cadastro conclui e loga
- [x] T04 Slice auth (login, logout, renovação, recuperação de senha, aceitar convite) — done when: fluxos funcionam contra a API
- [x] T05 Slice subscription + billing (assinatura, mudança de plano, cancelar/retomar/reativar, meio de pagamento, avisos) — done when: jornada US2 funciona
- [x] T06 Slice invoices (lista com cursor, detalhe, pagar agora) — done when: jornada US3 funciona
- [x] T07 Slices usage + team + api-keys + audit — done when: jornadas US4, US5 e US7 funcionam
- [x] T08 Slice playground — done when: CRUD de projetos pela sessão mostra a quota
- [x] T09 Compose + CI + README — done when: `docker compose up` sobe o portal e a CI builda/testa

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [x] Merge local em `develop` e `main`; push da `main` para o GitHub

## Done

- Verificado no compose com navegador: home com planos → cadastro (slug sugerido) → acompanhamento do provisionamento → login → visão geral → playground pela sessão (quota caindo a cada ação) → meio de pagamento refletido na assinatura.
- Pendente: conferência visual em 360 px (a janela do navegador de teste não pôde ser redimensionada) e testes E2E do portal no navegador (Playwright) para o SC-006.
