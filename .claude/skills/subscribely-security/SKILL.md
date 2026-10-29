---
name: subscribely-security
description: Segurança do Subscribely — JWT próprio com refresh rotativo, RBAC (PlatformAdmin/TenantAdmin/Member), API keys com hash, isolamento entre tenants, HMAC de webhooks, segredos, rate limit e prevenção de vazamento em logs. Use em autenticação, autorização, criptografia, validação de entrada e revisões de segurança.
---

# Segurança — Subscribely

## Autenticação
- **Senhas:** `PasswordHasher<User>` (PBKDF2). Mínimo de 12 caracteres. Login falho → 401 **genérico** (não diga se o e-mail existe).
- **Access JWT:** HS256, 15 min, claims `sub`, `tid` (ausente para PlatformAdmin), `role`, `jti`. Chave ≥ 256 bits via `Jwt:SigningKey` (env/user-secrets). Validar issuer, audience, lifetime, clock skew de 30 s.
- **Refresh:** 32 bytes aleatórios (`RandomNumberGenerator`), guardado como SHA-256. Rotação a cada uso; reusar um token já trocado → revogar **toda a família** + auditoria `auth.refresh_reuse_detected`.
- **API key:** `sk_live_{prefix8}_{secret32}` (base62). Guarda `prefix` + SHA-256 do segredo; exibe uma vez. Lookup pelo prefixo, comparação com `CryptographicOperations.FixedTimeEquals`. Aceita só o header `X-Api-Key`.
- **Rate limit de login:** `AddRateLimiter` com janela fixa de 5/min por IP+e-mail → 429.

## Autorização
- Políticas nomeadas: `PlatformAdmin`, `TenantAdmin`, `TenantMember` (Admin ou Member), `ApiKey`.
- Todo endpoint declara `.RequireAuthorization(policy)` explicitamente; endpoints anônimos usam `.AllowAnonymous()` de forma consciente.
- **Isolamento:** o tenant vem só de `ITenantContext`. Filtro global do EF por `TenantId` no Catálogo. Recurso de outro tenant → **404** (nunca 403, para não revelar a existência).
- Endpoints de produto aceitam só API key; endpoints de gestão aceitam só JWT.

## Webhooks
- `X-Subscribely-Signature: t={unix},v1={hex}`; HMAC-SHA256(secret, `{t}.{rawBody}`). Tolerância de 5 min. `FixedTimeEquals`. Falha → 401 sem detalhes.
- Leia o body bruto (`EnableBuffering`) **antes** de desserializar.

## Segredos e dados
- Nada de segredo em `appsettings*.json` commitado: use env vars, user-secrets em dev e `.env` (gitignored) no compose.
- Nunca logar senha, tokens, API keys, assinatura HMAC ou payload completo. E-mail em log → mascarado (`j***@dominio.com`).
- `[NotAudited]` em todo campo `*hash*`, `*token*`, `*secret*`.
- ProblemDetails sem stack trace fora de Development.

## Checklist de revisão de segurança
- [ ] Toda rota tem uma política explícita?
- [ ] Teste A×B de isolamento cobre a rota?
- [ ] Entradas validadas (tamanho, formato, enum)?
- [ ] Comparações de segredo em tempo constante?
- [ ] Nada sensível em log, auditoria, e-mail ou resposta?
- [ ] Slug/identificadores usados em SQL estão validados e escapados?

## Skills relacionadas
`dotnet-claude-kit:authentication`, `backend-design:auth-and-authorization`, `backend-design:security-discipline`, `security-review`.
