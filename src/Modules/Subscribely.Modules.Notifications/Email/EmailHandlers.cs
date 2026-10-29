using System.Globalization;
using Microsoft.Extensions.Options;
using Subscribely.Infrastructure.Outbox;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Metering.Contracts;
using Subscribely.Modules.Payments.Contracts;
using Subscribely.Modules.Tenancy.Contracts;

namespace Subscribely.Modules.Notifications.Email;

/// <summary>Dados comuns aos e-mails: nome do tenant e links do portal.</summary>
internal sealed class EmailContext(ITenancyApi tenancy, IOptions<NotificationsOptions> options)
{
    public async Task<string> CompanyAsync(Guid tenantId, CancellationToken ct) =>
        (await tenancy.GetTenantAsync(tenantId, ct))?.Name ?? "sua empresa";

    public string Link(string path) => new Uri(options.Value.PortalBaseUrl, path).ToString();
}

internal sealed class OnTrialEnding(EmailDispatcher emails, EmailContext context) : IIntegrationEventHandler<TrialEnding>
{
    public async Task HandleAsync(TrialEnding @event, CancellationToken cancellationToken) =>
        await emails.SendToAdminsAsync(@event.TenantId, "trial-ending", $"trial-ending:{@event.SubscriptionId}", new Dictionary<string, string>
        {
            ["company"] = await context.CompanyAsync(@event.TenantId, cancellationToken),
            ["trialEndsAt"] = PtBr.Date(@event.TrialEndsAt),
            ["link"] = context.Link("billing/payment-method"),
        }, cancellationToken);
}

internal sealed class OnInvoiceIssued(EmailDispatcher emails, EmailContext context) : IIntegrationEventHandler<InvoiceIssued>
{
    public async Task HandleAsync(InvoiceIssued @event, CancellationToken cancellationToken)
    {
        if (@event.TotalCents <= 0)
        {
            return;
        }

        await emails.SendToAdminsAsync(@event.TenantId, "invoice-issued", $"invoice-issued:{@event.InvoiceId}", new Dictionary<string, string>
        {
            ["company"] = await context.CompanyAsync(@event.TenantId, cancellationToken),
            ["number"] = @event.Number,
            ["total"] = PtBr.Money(@event.TotalCents),
            ["issuedAt"] = PtBr.DateTime(@event.IssuedAt),
            ["link"] = context.Link($"billing/invoices/{@event.InvoiceId}"),
        }, cancellationToken);
    }
}

internal sealed class OnInvoicePaid(EmailDispatcher emails, EmailContext context, IBillingApi billing) : IIntegrationEventHandler<InvoicePaid>
{
    public async Task HandleAsync(InvoicePaid @event, CancellationToken cancellationToken)
    {
        if (@event.TotalCents <= 0 || await billing.GetInvoiceAsync(@event.InvoiceId, cancellationToken) is not { } invoice)
        {
            return;
        }

        await emails.SendToAdminsAsync(@event.TenantId, "invoice-paid", $"invoice-paid:{@event.InvoiceId}", new Dictionary<string, string>
        {
            ["company"] = await context.CompanyAsync(@event.TenantId, cancellationToken),
            ["number"] = invoice.Number,
            ["total"] = PtBr.Money(@event.TotalCents),
            ["paidAt"] = PtBr.DateTime(@event.PaidAt),
            ["link"] = context.Link($"billing/invoices/{@event.InvoiceId}"),
        }, cancellationToken);
    }
}

internal sealed class OnPaymentFailed(EmailDispatcher emails, EmailContext context, IBillingApi billing) : IIntegrationEventHandler<PaymentFailed>
{
    public static string Reason(string failureCode) => failureCode switch
    {
        "card_declined" => "cartão recusado",
        "insufficient_funds" => "saldo insuficiente",
        "invalid_payment_method" => "meio de pagamento inválido",
        "gateway_unavailable" => "instabilidade no processador de pagamentos",
        _ => "motivo não informado pelo banco",
    };

    public async Task HandleAsync(PaymentFailed @event, CancellationToken cancellationToken)
    {
        if (await billing.GetInvoiceAsync(@event.InvoiceId, cancellationToken) is not { } invoice)
        {
            return;
        }

        var dedupKey = string.Create(CultureInfo.InvariantCulture, $"payment-failed:{@event.InvoiceId}:{@event.Attempt}");
        await emails.SendToAdminsAsync(@event.TenantId, "payment-failed", dedupKey, new Dictionary<string, string>
        {
            ["company"] = await context.CompanyAsync(@event.TenantId, cancellationToken),
            ["number"] = invoice.Number,
            ["total"] = PtBr.Money(invoice.TotalCents),
            ["reason"] = Reason(@event.FailureCode),
            ["nextAttempt"] = @event.NextAttemptAt is { } next
                ? $"Vamos tentar de novo em {PtBr.DateTime(next)}."
                : "Não haverá novas tentativas automáticas.",
            ["link"] = context.Link("billing/payment-method"),
        }, cancellationToken);
    }
}

internal sealed class OnSubscriptionSuspended(EmailDispatcher emails, EmailContext context) : IIntegrationEventHandler<SubscriptionSuspended>
{
    public async Task HandleAsync(SubscriptionSuspended @event, CancellationToken cancellationToken) =>
        await emails.SendToAdminsAsync(@event.TenantId, "subscription-suspended", $"suspended:{@event.SubscriptionId}:{@event.OccurredAt:O}", new Dictionary<string, string>
        {
            ["company"] = await context.CompanyAsync(@event.TenantId, cancellationToken),
            ["link"] = context.Link("billing"),
        }, cancellationToken);
}

/// <summary>Só para os TenantAdmins ativos (Members não recebem alertas de quota).</summary>
internal sealed class OnQuotaThresholdReached(EmailDispatcher emails, EmailContext context) : IIntegrationEventHandler<QuotaThresholdReached>
{
    public async Task HandleAsync(QuotaThresholdReached @event, CancellationToken cancellationToken)
    {
        var template = @event.Threshold >= 100 ? "quota-100" : "quota-80";
        var resetsAt = DateTimeOffset.ParseExact(@event.Period, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).AddMonths(1);
        var dedupKey = string.Create(CultureInfo.InvariantCulture, $"quota:{@event.TenantId}:{@event.Period}:{@event.Threshold}");
        await emails.SendToAdminsAsync(@event.TenantId, template, dedupKey, new Dictionary<string, string>
        {
            ["company"] = await context.CompanyAsync(@event.TenantId, cancellationToken),
            ["period"] = DateTimeOffset.ParseExact(@event.Period, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
                .ToString("MM/yyyy", CultureInfo.InvariantCulture),
            ["limit"] = @event.Limit.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")),
            ["resetsAt"] = PtBr.DateTime(resetsAt),
            ["link"] = context.Link("usage"),
        }, cancellationToken);
    }
}

/// <summary>Para o convidado. O token de uso único só aparece no link.</summary>
internal sealed class OnUserInvited(EmailDispatcher emails, EmailContext context) : IIntegrationEventHandler<UserInvited>
{
    public async Task HandleAsync(UserInvited @event, CancellationToken cancellationToken) =>
        await emails.SendAsync(@event.TenantId, "user-invitation", [new EmailRecipient(@event.Email, $"invite:{@event.InvitationId}")], new Dictionary<string, string>
        {
            ["company"] = await context.CompanyAsync(@event.TenantId, cancellationToken),
            ["expiresAt"] = PtBr.DateTime(@event.ExpiresAt),
            ["link"] = context.Link($"invitations/{Uri.EscapeDataString(@event.Token)}"),
        }, cancellationToken);
}

/// <summary>Para o próprio usuário. O token de uso único só aparece no link.</summary>
internal sealed class OnPasswordResetRequested(EmailDispatcher emails, EmailContext context) : IIntegrationEventHandler<PasswordResetRequested>
{
    public Task HandleAsync(PasswordResetRequested @event, CancellationToken cancellationToken) =>
        emails.SendAsync(null, "password-reset", [new EmailRecipient(@event.Email, $"password-reset:{@event.TokenId}")], new Dictionary<string, string>
        {
            ["expiresAt"] = PtBr.DateTime(@event.ExpiresAt),
            ["link"] = context.Link($"password-reset?token={Uri.EscapeDataString(@event.Token)}"),
        }, cancellationToken);
}
