# Spec — 07 Notifications (disparo de e-mails)

> Status: **APROVADO** — execução autônoma autorizada pelo humano em 2026-10-28. Herda as decisões de `specs/OVERVIEW.md`.

## Context and problem

Os tenants precisam ser avisados de eventos que afetam acesso e dinheiro. O envio não pode
bloquear as transações de negócio, não pode se perder quando o SMTP falha e não pode duplicar
e-mails quando eventos são reprocessados.

## Acceptance criteria (verifiable)

- [ ] Envio via SMTP (MailKit) para o Mailpit no compose; e-mails visíveis em `http://localhost:8025`.
- [ ] Consumidores do outbox enviam um e-mail aos **TenantAdmins ativos** do tenant para:

  | Evento | Template | Chave de deduplicação |
  |---|---|---|
  | `TrialEnding` | `trial-ending` | `trial-ending:{subscriptionId}` |
  | `InvoiceIssued` (total > 0) | `invoice-issued` | `invoice-issued:{invoiceId}` |
  | `InvoicePaid` (total > 0) | `invoice-paid` | `invoice-paid:{invoiceId}` |
  | `PaymentFailed` | `payment-failed` | `payment-failed:{invoiceId}:{attempt}` |
  | `SubscriptionSuspended` | `subscription-suspended` | `suspended:{subscriptionId}:{suspendedAt}` |
  | `QuotaThresholdReached` | `quota-80` / `quota-100` | `quota:{tenantId}:{period}:{threshold}` |
  | `UserInvited` (funcional, spec 01) | `user-invitation` → para o convidado | `invite:{invitationId}` |
  | `PasswordResetRequested` (funcional, spec 01) | `password-reset` → para o próprio usuário | `password-reset:{tokenId}` |

- [ ] Deduplicação por `notifications.email_log.dedup_key` UNIQUE, gravado **antes** do envio no mesmo fluxo idempotente: um evento reprocessado não reenvia.
- [ ] Falha de SMTP → o outbox reprocessa (até 5×); depois disso o registro fica `Failed` com o erro, sem derrubar nada.
- [ ] Templates HTML + texto puro embutidos no assembly, em pt-BR, com placeholders `{{nome}}` **sempre HTML-encoded**.
- [ ] Valores monetários formatados `R$ 1.234,56` e datas em `America/Sao_Paulo` no corpo do e-mail.
- [ ] Nenhum e-mail contém senha, token de acesso ou API key; o convite e o reset de senha contêm só o link com o token de uso único.
- [ ] Alertas de quota (80%/100%) vão **só para os TenantAdmins** ativos; Members não recebem.
- [ ] `GET /admin/notifications?tenantId=&status=` lista o log de envios (PlatformAdmin).

## Out of scope (explicit)

- SMS, push, webhooks de saída para o cliente, preferências de notificação por usuário, i18n além de pt-BR.

## Data contracts

- **Catálogo `notifications`:** `email_log(id uuid, tenant_id NULL, template, to_address, subject, dedup_key UNIQUE, status[Pending|Sent|Failed], attempts, last_error NULL, created_at, sent_at NULL)`
- **Configuração:** `Smtp:Host`, `Smtp:Port`, `Smtp:From` (`no-reply@subscribely.local`), `Smtp:UseTls`.
- **Contracts consumidos:** `ITenancyApi.ListActiveAdmins(tenantId)`, `IBillingApi.GetInvoice(id)`.
- **External dependencies:** servidor SMTP (Mailpit local).

## Authorization

- Sem endpoints de tenant. Log administrativo: PlatformAdmin.

## Edge cases and failure modes

| Caso | Comportamento esperado |
|---|---|
| Tenant sem TenantAdmin ativo | Nada é enviado; log de warning |
| Mailpit fora do ar | Retry do outbox; `ready` não é afetado (o SMTP não é dependência crítica) |
| Evento de fatura com total 0 | Não envia |
| Placeholder com HTML malicioso (nome da empresa) | Renderizado como texto (encoded) |

## Decisions taken

| Decisão | Alternativa rejeitada | Por quê |
|---|---|---|
| MailKit | `System.Net.Mail.SmtpClient` | O `SmtpClient` está marcado como obsoleto para uso novo |
| Templates simples com replace + encode | Razor/Scriban | YAGNI; poucos templates |
| Dedup em tabela | Dedup em Redis | Durável e auditável |

## Constraints

- Envio desacoplado: nenhuma transação de negócio espera o SMTP.

## Open questions (risks)

- Nenhuma. Resolvida: os e-mails de quota vão só para os TenantAdmins.
