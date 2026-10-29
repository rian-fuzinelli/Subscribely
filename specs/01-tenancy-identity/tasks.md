# Tasks — 01 Tenancy & Identity

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `feat/tenancy-identity` (base: `develop`)

## Tasks

- [x] T01 @database-engineer `TenancyDbContext` + configurações (tenants, users, refresh_tokens, invitations, api_keys) — done when: modelo valida no teste de integração
- [x] T02 [P] @security-engineer Entidades de domínio Tenant/User/Invitation com regras (último admin, expiração) — done when: testes unitários verdes (deps: T01)
- [x] T03 [P] @security-engineer `JwtTokenService` (access + refresh rotativo com família) e políticas RBAC — done when: testes de emissão, expiração e reuso verdes (deps: T01)
- [x] T04 @database-engineer `TenantDatabaseProvisioner` idempotente (CREATE DATABASE + migrations do tenant) — done when: rodar 2× termina sem erro no Testcontainers (deps: T01)
- [x] T05 @background-jobs-engineer `ProvisionTenantJob` com 3 tentativas → `ProvisioningFailed`; publica `TenantProvisioned` — done when: teste de falha e sucesso verdes (deps: T04)
- [x] T06 @security-engineer Slice Signup (202) com validação e 409 de conflito — done when: teste de integração signup → `Active` verde (deps: T03, T05)
- [x] T07 @security-engineer Slices Login / Refresh / Logout + rate limit de login — done when: testes 401, rotação, reuso e 429 verdes (deps: T03, T06)
- [x] T08 [P] @security-engineer API keys (criar, listar, revogar) + `ApiKeyAuthenticationHandler` com cache Redis 60 s — done when: key revogada → 401 no teste (deps: T07)
- [x] T09 [P] @security-engineer Convites + aceitar + limite de seats (ativos + convites pendentes) via `IBillingApi` (stub até a spec 02) — done when: 409 `seat-limit-reached` no teste e convite expirado libera o assento (deps: T07)
- [x] T10 [P] @security-engineer Recuperação de senha (pedido 202 genérico, token de 1 h com hash, confirmação revoga os refresh tokens, limite de 3/h) — done when: testes de token válido/expirado/reusado e de e-mail inexistente verdes (deps: T07)
- [x] T11 @test-engineer Suíte de isolamento A×B em todos os endpoints do módulo — done when: todos devolvem 404 cruzado (deps: T08, T09, T10)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [x] Ampliar testes/CI para cobrir as mudanças — done when: pipeline verde
- [x] Push da branch para a origem — done when: branch publicada
- [x] PR draft em `docs/prs/feat-tenancy-identity.md`; merge local em `develop`

## Done
