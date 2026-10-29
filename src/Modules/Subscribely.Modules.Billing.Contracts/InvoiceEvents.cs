using Subscribely.SharedKernel;

namespace Subscribely.Modules.Billing.Contracts;

/// <summary>Fatura emitida. Total &gt; 0 → o módulo Payments cobra na hora (sem prazo de vencimento).</summary>
public sealed record InvoiceIssued(
    Guid InvoiceId,
    Guid TenantId,
    Guid SubscriptionId,
    string Number,
    string Reason,
    long TotalCents,
    DateTimeOffset IssuedAt) : IIntegrationEvent;

/// <summary>Fatura paga (cobrança confirmada ou total zero): leva <c>PastDue</c>/<c>Suspended</c> → <c>Active</c>.</summary>
public sealed record InvoicePaid(Guid InvoiceId, Guid TenantId, Guid SubscriptionId, long TotalCents, DateTimeOffset PaidAt) : IIntegrationEvent;

public sealed record InvoiceVoided(Guid InvoiceId, Guid TenantId, Guid SubscriptionId, string Reason, DateTimeOffset VoidedAt) : IIntegrationEvent;
