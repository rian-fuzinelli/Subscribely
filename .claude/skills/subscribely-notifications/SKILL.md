---
name: subscribely-notifications
description: Disparo de notificações por e-mail do Subscribely — consumidores de eventos do outbox, deduplicação por dedup_key, envio SMTP com MailKit para o Mailpit, templates HTML/texto em pt-BR com encode obrigatório, formatação de moeda e fuso, e testes contra o Mailpit. Use ao criar ou alterar qualquer e-mail ou notificação.
---

# Notificações por e-mail — Subscribely

## Fluxo de envio (idempotente)
1. O consumidor do outbox recebe o evento (`IIntegrationEventHandler<T>`).
2. Resolve os destinatários: `ITenancyApi.ListActiveAdmins(tenantId)` (exceto o convite, que vai para o convidado).
3. **Reserva** em `notifications.email_log` com a `dedup_key` (insert; conflito `23505` → já tratado → retorna).
4. Renderiza o template e envia via `IEmailSender` (MailKit).
5. Marca `Sent` (ou `Failed` + `last_error`; o outbox reprocessa os `Pending`/`Failed` com `attempts < 5`).

## Chaves de deduplicação
| Evento | Template | dedup_key |
|---|---|---|
| TrialEnding | trial-ending | `trial-ending:{subscriptionId}` |
| InvoiceIssued (>0) | invoice-issued | `invoice-issued:{invoiceId}` |
| InvoicePaid (>0) | invoice-paid | `invoice-paid:{invoiceId}` |
| PaymentFailed | payment-failed | `payment-failed:{invoiceId}:{attempt}` |
| SubscriptionSuspended | subscription-suspended | `suspended:{subscriptionId}:{suspendedAt:O}` |
| QuotaThresholdReached | quota-80 / quota-100 | `quota:{tenantId}:{period}:{threshold}` |
| UserInvited | user-invitation | `invite:{invitationId}` |
| PasswordResetRequested | password-reset | `password-reset:{tokenId}` |
(Com vários admins, a chave recebe `:{userId}` no final.)

## Templates
- `Templates/{nome}.html` + `{nome}.txt` como `EmbeddedResource`; assunto na 1ª linha do `.txt` (`Subject: ...`).
- Placeholders `{{campo}}`: **sempre** `HtmlEncoder.Default.Encode` no HTML. Nunca concatenar HTML com dado do usuário.
- Moeda: `(cents / 100m).ToString("C", new CultureInfo("pt-BR"))` → `R$ 1.234,56`. Datas: converter para `America/Sao_Paulo` só na renderização.
- Tom: claro, curto, com uma ação (link) por e-mail. Sem segredos, tokens de acesso ou API keys. O convite leva só o link com o token de uso único.

## SMTP
- `SmtpOptions { Host, Port, From, UseTls, Enabled }`. Compose: `mailpit:1025`, UI em `http://localhost:8025`.
- `Enabled=false` → grava `Skipped` (sem envio). Timeout de 10 s por envio.

## Testes
- Unit: renderer (encode de `<script>`, moeda, fuso), mapeamento evento → template/destinatários.
- Integration: Mailpit via Testcontainers; conferir pela API `GET /api/v1/messages`; reprocessar o evento = 1 e-mail.

## Skills relacionadas
`backend-design:idempotency-and-side-effects`, `design:ux-copy` (texto dos e-mails).
