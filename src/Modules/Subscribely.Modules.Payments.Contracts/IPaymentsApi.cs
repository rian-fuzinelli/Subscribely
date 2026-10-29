using Subscribely.SharedKernel;

namespace Subscribely.Modules.Payments.Contracts;

/// <summary>API síncrona do módulo Payments para os demais módulos.</summary>
public interface IPaymentsApi
{
    /// <summary>O tenant tem um meio de pagamento padrão cadastrado.</summary>
    Task<bool> HasDefaultPaymentMethodAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>Primeira falha de cobrança de uma fatura: a assinatura vai para <c>PastDue</c>.</summary>
public sealed record PaymentFailed(
    Guid TenantId,
    Guid InvoiceId,
    int Attempt,
    string FailureCode,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>A última retentativa do dunning falhou: a assinatura vai para <c>Suspended</c>.</summary>
public sealed record PaymentRetriesExhausted(Guid TenantId, Guid InvoiceId, DateTimeOffset OccurredAt) : IIntegrationEvent;

/// <summary>Cobrança confirmada pelo gateway (a fatura já foi marcada como paga).</summary>
public sealed record PaymentSucceeded(Guid TenantId, Guid InvoiceId, Guid ChargeId, long AmountCents, DateTimeOffset OccurredAt) : IIntegrationEvent;
