# Tasks — 07 Notifications

> Legenda: `[ ]` pendente · `[~]` em andamento · `[x]` concluída. Formato: `ORCHESTRATION.md`.

**Branch:** `feat/email-notifications` (base: `develop`)

## Tasks

- [x] T01 [P] @database-engineer `NotificationsDbContext` + `email_log` com `dedup_key` UNIQUE — done when: modelo valida no teste
- [x] T02 [P] @notifications-engineer `MailKitEmailSender` + `SmtpOptions` — done when: o teste com Mailpit recebe a mensagem
- [x] T03 [P] @notifications-engineer `TemplateRenderer` + os 9 templates (HTML + texto, pt-BR) — done when: testes de encode, moeda e fuso verdes
- [x] T04 @notifications-engineer Pipeline idempotente reservar → enviar → marcar — done when: reprocessar o evento = 1 e-mail (deps: T01, T02, T03)
- [x] T05 @notifications-engineer Consumidores dos 8 eventos — done when: um teste por evento confere o destinatário e o template (deps: T04)
- [x] T06 [P] @notifications-engineer Endpoint admin de log — done when: teste com RBAC verde (deps: T01)
- [x] T07 @security-engineer Revisão: nenhum segredo nos templates/logs; link de convite de uso único — done when: checklist da skill de segurança atendido (deps: T05)

## Closing (fase *close* do `dev-lifecycle` — sempre por último)

- [x] Ampliar testes/CI — done when: pipeline verde
- [ ] ⏸ Push — bloqueado: sem `origin`
- [x] PR draft em `docs/prs/feat-email-notifications.md`; merge local em `develop`

## Done

- Desvios: os links dos e-mails usam `Notifications:PortalBaseUrl` (padrão `http://localhost:3000/`, o portal é spec futura); status extra `Skipped` com `Smtp:Enabled = false` (previsto no plan); a falha de SMTP lança `EmailDeliveryException` para o outbox reprocessar e o registro para de tentar após 5 tentativas.
