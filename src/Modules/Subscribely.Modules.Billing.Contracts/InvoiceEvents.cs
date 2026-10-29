using Subscribely.SharedKernel;

namespace Subscribely.Modules.Billing.Contracts;

/// <summary>Fatura paga (cobrança confirmada ou total zero): leva <c>PastDue</c>/<c>Suspended</c> → <c>Active</c>.</summary>
public sealed record InvoicePaid(Guid InvoiceId, Guid TenantId, Guid SubscriptionId, long TotalCents, DateTimeOffset PaidAt) : IIntegrationEvent;
