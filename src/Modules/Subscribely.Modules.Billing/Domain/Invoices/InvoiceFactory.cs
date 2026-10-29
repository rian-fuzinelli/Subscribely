using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Domain.Subscriptions;

namespace Subscribely.Modules.Billing.Domain.Invoices;

/// <summary>Período e linhas de cada motivo de fatura (funções puras; a numeração fica no emissor).</summary>
internal static class InvoiceFactory
{
    /// <summary>TrialConversion, Renewal e Reactivation: o período atual inteiro no plano atual.</summary>
    public static (BillingPeriod Period, IReadOnlyList<InvoiceLineDraft> Lines) FullPeriod(Subscription subscription, PlanVersion version)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var period = subscription.CurrentPeriod;
        return (period, [new InvoiceLineDraft(Label(version, subscription.Interval), version.Id, PlanChangeClassifier.PriceOf(version, subscription.Interval), period)]);
    }

    /// <summary>
    /// Upgrade já aplicado em <paramref name="subscription"/>. Mesmo intervalo: crédito e cobrança da fração restante,
    /// período <c>[now, fim)</c>. Mensal → anual: crédito do mensal não usado + o novo período anual cheio.
    /// </summary>
    public static (BillingPeriod Period, IReadOnlyList<InvoiceLineDraft> Lines) Upgrade(
        Subscription subscription, PlanChange change, PlanVersion previous, PlanVersion current, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(change);

        var oldPrice = PlanChangeClassifier.PriceOf(previous, change.PreviousInterval);
        var newPrice = PlanChangeClassifier.PriceOf(current, subscription.Interval);
        var unused = new BillingPeriod(Min(now, change.PreviousPeriod.End), change.PreviousPeriod.End);
        var credit = $"Crédito não utilizado: {Label(previous, change.PreviousInterval)}";

        if (change.PreviousInterval == subscription.Interval)
        {
            var proration = ProrationCalculator.SameInterval(oldPrice, newPrice, change.PreviousPeriod, now);
            return (unused,
            [
                new InvoiceLineDraft(credit, previous.Id, proration.CreditCents, unused),
                new InvoiceLineDraft($"{Label(current, subscription.Interval)} (proporcional)", current.Id, proration.ChargeCents, unused),
            ]);
        }

        var newPeriod = ProrationCalculator.NewPeriod(oldPrice, newPrice, change.PreviousPeriod, now);
        return (subscription.CurrentPeriod,
        [
            new InvoiceLineDraft(credit, previous.Id, newPeriod.CreditCents, unused),
            new InvoiceLineDraft(Label(current, subscription.Interval), current.Id, newPeriod.ChargeCents, subscription.CurrentPeriod),
        ]);
    }

    private static string Label(PlanVersion version, BillingInterval interval) =>
        $"Plano {version.Name} ({(interval == BillingInterval.Annual ? "anual" : "mensal")})";

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
