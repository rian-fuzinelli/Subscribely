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

/// <summary>API síncrona do módulo Billing para os demais módulos.</summary>
public interface IBillingApi
{
    /// <summary>Versão vigente de um plano <b>ativo</b>; <c>null</c> se não existir ou estiver arquivado.</summary>
    Task<PlanVersionInfo?> GetActivePlanVersionAsync(string planCode, CancellationToken cancellationToken);
}
