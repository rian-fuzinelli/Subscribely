using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subscribely.Infrastructure.Persistence;
using Subscribely.Modules.Notifications.Persistence;
using Subscribely.Modules.Tenancy.Contracts;

namespace Subscribely.Modules.Notifications.Email;

/// <summary>Um destinatário de um e-mail; <see cref="DedupKey"/> é única por destinatário e evento.</summary>
internal sealed record EmailRecipient(string Address, string DedupKey);

/// <summary>
/// Pipeline idempotente: reserva a <c>dedup_key</c> em <c>email_log</c> → renderiza → envia → marca.
/// Já enviado = no-op. Falha de SMTP → <c>Failed</c> + exceção para o outbox reprocessar (até 5×).
/// </summary>
internal sealed partial class EmailDispatcher(
    NotificationsDbContext db,
    TemplateRenderer renderer,
    IEmailSender sender,
    ITenancyApi tenancy,
    IOptions<SmtpOptions> smtp,
    TimeProvider time,
    ILogger<EmailDispatcher> logger)
{
    /// <summary>Para todos os TenantAdmins ativos; a chave recebe <c>:{userId}</c> por destinatário.</summary>
    public async Task SendToAdminsAsync(Guid tenantId, string template, string dedupKey, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        var admins = await tenancy.ListActiveAdminsAsync(tenantId, ct);
        if (admins.Count == 0)
        {
            LogNoAdmins(logger, tenantId, template);
            return;
        }

        await SendAsync(tenantId, template, admins.Select(a => new EmailRecipient(a.Email, $"{dedupKey}:{a.UserId}")).ToList(), values, ct);
    }

    public async Task SendAsync(Guid? tenantId, string template, IReadOnlyList<EmailRecipient> recipients, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        var rendered = renderer.Render(template, values);
        var failures = new List<string>();

        foreach (var recipient in recipients)
        {
            var log = await ReserveAsync(tenantId, template, recipient, rendered.Subject, ct);
            if (log.IsSettled)
            {
                continue;
            }

            if (!smtp.Value.Enabled)
            {
                log.MarkSkipped();
            }
            else
            {
                try
                {
                    await sender.SendAsync(new EmailMessage(recipient.Address, rendered.Subject, rendered.Html, rendered.Text), ct);
                    log.MarkSent(time.GetUtcNow());
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.MarkFailed($"{ex.GetType().Name}: {ex.Message}");
                    failures.Add(log.DedupKey);
                    LogSendFailed(logger, ex, template, log.Id, log.Attempts);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        if (failures.Count > 0)
        {
            throw new EmailDeliveryException(template, failures);
        }
    }

    private async Task<EmailLog> ReserveAsync(Guid? tenantId, string template, EmailRecipient recipient, string subject, CancellationToken ct)
    {
        if (await db.EmailLog.FirstOrDefaultAsync(e => e.DedupKey == recipient.DedupKey, ct) is { } existing)
        {
            return existing;
        }

        var log = EmailLog.Reserve(tenantId, template, recipient.Address, subject, recipient.DedupKey, time.GetUtcNow());
        db.EmailLog.Add(log);
        try
        {
            await db.SaveChangesAsync(ct);
            return log;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Entrega concorrente do mesmo evento: a outra execução reservou primeiro.
            db.Entry(log).State = EntityState.Detached;
            return await db.EmailLog.FirstAsync(e => e.DedupKey == recipient.DedupKey, ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No active TenantAdmin for tenant {TenantId}; e-mail {Template} not sent")]
    private static partial void LogNoAdmins(ILogger logger, Guid tenantId, string template);

    [LoggerMessage(Level = LogLevel.Warning, Message = "E-mail {Template} ({EmailLogId}) failed on attempt {Attempt}")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, string template, Guid emailLogId, int attempt);
}

internal sealed class EmailDeliveryException(string template, IReadOnlyList<string> dedupKeys)
    : Exception($"E-mail {template} failed for {dedupKeys.Count} recipient(s); the outbox will retry.");
