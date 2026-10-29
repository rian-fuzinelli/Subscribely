# Spec — 01 Tenancy & Identity

> Status: **APROVADO** — execução autônoma autorizada pelo humano em 2026-10-28. Herda as decisões de `specs/OVERVIEW.md`.

## Context and problem

Organizações precisam se cadastrar sozinhas (signup com trial), ter um banco de dados próprio
provisionado automaticamente, convidar usuários dentro do limite de assentos e autenticar com
segurança, por usuário (JWT) ou por máquina (API key). Um vazamento entre tenants é a falha mais
grave possível do produto.

## Acceptance criteria (verifiable)

- [ ] `POST /v1/signup` cria o tenant (`Provisioning`), o usuário TenantAdmin e enfileira o job de provisionamento; responde 202 com `tenantId` e `status`.
- [ ] O job de provisionamento cria `subscribely_t_{slug}`, aplica as migrations do tenant, marca o tenant como `Active` e publica `TenantProvisioned` no outbox (que inicia o trial na spec 03).
- [ ] O provisionamento é idempotente: reexecutar após falha parcial termina em `Active` sem erro.
- [ ] `POST /v1/auth/login` devolve `accessToken` (JWT 15 min, claims `sub`, `tid`, `role`) e `refreshToken` (7 dias); falha devolve 401 genérico.
- [ ] O login só é permitido com o tenant `Active`; PlatformAdmin não tem `tid`.
- [ ] `POST /v1/auth/refresh` rotaciona o token. Reusar um refresh já trocado revoga **toda a família** e devolve 401.
- [ ] `POST /v1/auth/logout` revoga a família do refresh token atual.
- [ ] Login limitado a 5 tentativas por minuto por IP+e-mail (429 depois).
- [ ] TenantAdmin convida usuários (`POST /v1/users/invitations`); o convite expira em 72 h; aceitar (`POST /v1/invitations/{token}/accept`) define a senha e ativa o usuário.
- [ ] **Assentos ocupados = usuários `Active` + convites pendentes não expirados.** Convidar além de `max_seats` devolve 409 `seat-limit-reached` (limite via `Billing.Contracts`). Usuário `Disabled` ou convite expirado/revogado libera o assento na hora.
- [ ] `POST /v1/auth/password-reset` `{ email }` sempre responde **202 genérico** (não revela se o e-mail existe); se existir e estiver `Active`, publica `PasswordResetRequested` com um token de uso único válido por 1 h (guardado como SHA-256).
- [ ] `POST /v1/auth/password-reset/confirm` `{ token, newPassword }` troca a senha, invalida o token e **revoga todos os refresh tokens** do usuário; token inválido, expirado ou usado → 400 `invalid-reset-token`.
- [ ] Pedidos de reset limitados a 3 por hora por e-mail (o excedente é ignorado em silêncio, ainda com 202).
- [ ] TenantAdmin cria (`POST /v1/api-keys`), lista e revoga API keys; o segredo é exibido **uma única vez**.
- [ ] Requisição com `X-Api-Key` válida resolve o tenant; uma key revogada devolve 401 em até 60 s (TTL do cache).
- [ ] Teste prova que um usuário do tenant A recebe 404 ao acessar qualquer recurso do tenant B.

## Out of scope (explicit)

- SSO/OIDC, MFA, troca de e-mail, usuário em mais de um tenant.

## Data contracts

- **`POST /v1/signup`** in: `{ companyName: string(2..100), slug: string(^[a-z0-9-]{3,40}$), adminEmail: email, password: string(>=12), planCode: string, interval: "monthly"|"annual" }` → 202 `{ tenantId: uuid, status: "Provisioning" }`
- **`POST /v1/auth/login`** in: `{ email, password }` → 200 `{ accessToken, expiresIn: 900, refreshToken }`
- **`POST /v1/api-keys`** in: `{ name: string(1..60) }` → 201 `{ id, name, key: "sk_live_<prefix8>_<secret32>", createdAt }`
- **Catálogo `tenancy`:**
  - `tenants(id uuid PK, slug citext UNIQUE, name, status[Provisioning|Active|ProvisioningFailed], database_name, created_at)`
  - `users(id uuid PK, tenant_id uuid NULL FK, email citext UNIQUE, password_hash, role[PlatformAdmin|TenantAdmin|Member], status[Invited|Active|Disabled], created_at)`
  - `refresh_tokens(id, user_id FK, family_id uuid, token_hash UNIQUE, expires_at, revoked_at NULL, replaced_by_id NULL)`
  - `invitations(id, tenant_id, email, role, token_hash UNIQUE, expires_at, accepted_at NULL)`
  - `api_keys(id, tenant_id, name, prefix char(8) UNIQUE, key_hash, created_by, created_at, last_used_at NULL, revoked_at NULL)`
  - `password_reset_tokens(id, user_id FK, token_hash UNIQUE, expires_at, used_at NULL, created_at)`
- **Eventos (outbox):** `TenantProvisioned { tenantId, planCode, interval }`, `UserActivated { tenantId, userId }`, `UserInvited { tenantId, invitationId, email, token }`, `PasswordResetRequested { tokenId, userId, email, token }` (o token só trafega no outbox até o e-mail; nunca é logado).
- **Contracts expostos:** `ITenancyApi.GetTenant(tenantId)`, `ITenancyApi.CountActiveSeats(tenantId)`.

## Authorization

| Endpoint | Quem |
|---|---|
| signup, login, refresh, aceitar convite, password-reset (pedir/confirmar) | anônimo |
| usuários, convites, API keys (escrita) | TenantAdmin do próprio tenant |
| `GET /v1/tenant`, `GET /v1/users` | TenantAdmin, Member |
| `GET /admin/tenants` | PlatformAdmin |

Recurso de outro tenant: **404** (não revela a existência). Papel insuficiente: 403 genérico.

## Edge cases and failure modes

| Caso | Comportamento esperado |
|---|---|
| Slug ou e-mail já existente | 409 `conflict` com campo indicado (sem revelar a qual tenant pertence o e-mail) |
| Banco do tenant já existe (retry) | Provisionamento pula o `CREATE DATABASE` e só aplica migrations |
| Provisionamento falha 3 vezes | Status `ProvisioningFailed`, log de erro, auditoria; login devolve 403 `tenant-unavailable` |
| Refresh token expirado | 401 |
| Convite expirado ou já aceito | 410 `invitation-expired` |
| Removendo o último TenantAdmin | 409 `last-admin` |
| API key em URL/query | Ignorada; só aceita no header |
| Convite para e-mail já cadastrado (em qualquer tenant) | 409 `email-unavailable` genérico |
| Reset de senha para usuário `Invited`/`Disabled` | 202 genérico, nenhum e-mail enviado |

## Decisions taken

| Decisão | Alternativa rejeitada | Por quê |
|---|---|---|
| Provisionamento assíncrono (Hangfire) | Síncrono no request | `CREATE DATABASE` + migrations é lento e pode falhar; retry automático |
| `PasswordHasher<T>` do ASP.NET Identity (PBKDF2) | BCrypt externo | Nativo, sem dependência |
| JWT HS256 com chave ≥ 256 bits via env | RS256 | Um único emissor/validador; rotação de chave é uma pergunta em aberto |
| API key = prefixo + segredo com SHA-256 | Guardar a key criptografada | A key nunca precisa ser recuperada; o hash basta |
| E-mail único global | E-mail único por tenant | Login sem informar o tenant |

## Constraints

- Nunca logar senha, token ou API key. `password_hash` e `key_hash` nunca saem da API.
- Comparação de hashes em tempo constante.

## Open questions (risks)

- Nenhuma. Resolvidas: a recuperação de senha entra na v1; o e-mail é único global (um usuário, um tenant); assentos = ativos + convites pendentes, e `Disabled` libera na hora.
