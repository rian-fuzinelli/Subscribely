using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Domain.Plans;

namespace Subscribely.Modules.Billing.Domain.Subscriptions;

internal enum PlanChangeKind
{
    /// <summary>Preço diário maior ou mensal → anual: aplica na hora com fatura de proração.</summary>
    Upgrade,

    /// <summary>Preço diário menor ou anual → mensal: agendado para o fim do período.</summary>
    Downgrade,

    /// <summary>Mesmo preço diário: aplica na hora, sem fatura.</summary>
    Lateral,
}

/// <summary>
/// Classifica a mudança de plano pelo preço diário normalizado. A troca de intervalo decide sozinha
/// (mensal → anual = upgrade); no mesmo intervalo a duração se cancela e basta comparar os preços.
/// </summary>
internal static class PlanChangeClassifier
{
    public static PlanChangeKind Classify(PlanVersion current, BillingInterval currentInterval, PlanVersion target, BillingInterval targetInterval)
    {
        if (currentInterval != targetInterval)
        {
            return targetInterval == BillingInterval.Annual ? PlanChangeKind.Upgrade : PlanChangeKind.Downgrade;
        }

        var comparison = PriceOf(target, targetInterval).CompareTo(PriceOf(current, currentInterval));
        return comparison switch
        {
            > 0 => PlanChangeKind.Upgrade,
            < 0 => PlanChangeKind.Downgrade,
            _ => PlanChangeKind.Lateral,
        };
    }

    public static long PriceOf(PlanVersion version, BillingInterval interval)
    {
        ArgumentNullException.ThrowIfNull(version);
        return interval == BillingInterval.Annual ? version.AnnualPriceCents : version.MonthlyPriceCents;
    }
}
