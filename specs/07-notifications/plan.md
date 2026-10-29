# Plan — 07 Notifications

## Branch and delivery

- **Branch:** `feat/email-notifications` (base: `develop`)
- **PR slicing:** PR único.

## Technical approach

Módulo `Subscribely.Modules.Notifications` com um consumidor de outbox por evento. Cada consumidor
monta o modelo, reserva a `dedup_key` em `email_log` (insert; conflito = já tratado) e chama
`IEmailSender` (MailKit). `ITemplateRenderer` carrega os recursos embutidos e aplica o encode.
Os eventos ainda não implementados por outros módulos são testados com eventos sintéticos.

## Affected files (by layer)

| Camada | Arquivo | Mudança |
|---|---|---|
| Envio | `src/Modules/Subscribely.Modules.Notifications/Email/{IEmailSender.cs, MailKitEmailSender.cs, SmtpOptions.cs}` | SMTP |
| Templates | `.../Templates/*.html`, `*.txt`, `TemplateRenderer.cs` | Embutidos |
| Consumidores | `.../Handlers/On{TrialEnding,InvoiceIssued,InvoicePaid,PaymentFailed,SubscriptionSuspended,QuotaThresholdReached,UserInvited,PasswordResetRequested}.cs` | Um por evento |
| Persistência | `.../Persistence/{NotificationsDbContext.cs, Configurations/EmailLogConfiguration.cs}` | Schema `notifications` |
| Admin | `.../Features/ListNotifications/*` | |

## Risks and mitigation

| Risco | Mitigação |
|---|---|
| E-mail duplicado em reprocessamento | `dedup_key` UNIQUE reservada antes do envio |
| Reserva feita e envio falhou | Status `Pending` + o outbox reprocessa e reenvia apenas os `Pending`/`Failed` com `attempts < 5` |

## Rollback and reversibility

- Migration aditiva. Desligar o envio: `Smtp:Enabled = false` (grava como `Skipped`).

## Testing strategy

- Unit: renderer (encode, moeda, fuso), mapeamento evento → template/destinatários.
- Integration: container Mailpit no Testcontainers + API HTTP do Mailpit para conferir o recebimento; dedup em reprocessamento; falha do SMTP → retry.
