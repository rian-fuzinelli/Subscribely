namespace Subscribely.Modules.Billing.Contracts;

public enum BillingInterval
{
    Monthly,
    Annual,
}

/// <summary>Versão vigente de um plano ativo, como exposta para outros módulos.</summary>
public sealed record PlanVersionInfo(
    Guid PlanVersionId,
    string PlanCode,
    string Name,
    int Version,
    long MonthlyPriceCents,
    long AnnualPriceCents,
    int RequestQuotaMonthly,
    int MaxSeats);

/// <summary>
/// Limites vigentes de um tenant. <see cref="MaxSeats"/> já considera um downgrade pendente (menor dos dois).
/// <see cref="ProductAccessAllowed"/> é falso em Expired, Suspended e Canceled.
/// </summary>
public sealed record Entitlements(
    string SubscriptionStatus,
    int RequestQuotaMonthly,
    int MaxSeats,
    bool ProductAccessAllowed);

/// <summary>Fatura como exposta para outros módulos (o Payments cobra por ela).</summary>
public sealed record InvoiceInfo(
    Guid InvoiceId,
    Guid TenantId,
    Guid SubscriptionId,
    string Number,
    string Status,
    long TotalCents,
    DateTimeOffset IssuedAt);

public enum MarkInvoicePaidOutcome
{
    Paid,
    AlreadyPaid,

    /// <summary>A fatura foi anulada antes do pagamento: reconciliar manualmente.</summary>
    Void,
    NotFound,
}

/// <summary>API síncrona do módulo Billing para os demais módulos.</summary>
public interface IBillingApi
{
    /// <summary>Versão vigente de um plano <b>ativo</b>; <c>null</c> se não existir ou estiver arquivado.</summary>
    Task<PlanVersionInfo?> GetActivePlanVersionAsync(string planCode, CancellationToken cancellationToken);

    /// <summary>Limites da assinatura do tenant; <c>null</c> se o tenant ainda não tem assinatura.</summary>
    Task<Entitlements?> GetEntitlementsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<InvoiceInfo?> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>Faturas <c>Open</c> do tenant, mais antigas primeiro.</summary>
    Task<IReadOnlyList<InvoiceInfo>> ListOpenInvoicesAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Idempotente: a segunda chamada devolve <see cref="MarkInvoicePaidOutcome.AlreadyPaid"/> e não publica outro evento.</summary>
    Task<MarkInvoicePaidOutcome> MarkInvoicePaidAsync(Guid invoiceId, DateTimeOffset paidAt, CancellationToken cancellationToken);
}
