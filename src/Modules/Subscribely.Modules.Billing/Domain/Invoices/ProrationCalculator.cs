using Subscribely.Modules.Billing.Domain.Subscriptions;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Billing.Domain.Invoices;

/// <summary>Valores de uma proração: crédito (≤ 0) do plano antigo e cobrança (≥ 0) do novo.</summary>
internal readonly record struct Proration(long CreditCents, long ChargeCents)
{
    public long TotalCents => CreditCents + ChargeCents;
}

/// <summary>Proração por segundos, arredondamento half-up para centavos (sem ponto flutuante).</summary>
internal static class ProrationCalculator
{
    /// <summary>Upgrade no mesmo intervalo: só a fração restante do período, nos dois planos.</summary>
    public static Proration SameInterval(long oldPriceCents, long newPriceCents, BillingPeriod period, DateTimeOffset now)
    {
        var (remaining, total) = Fraction(period, now);
        return new Proration(
            -new Money(oldPriceCents).Fraction(remaining, total).Cents,
            new Money(newPriceCents).Fraction(remaining, total).Cents);
    }

    /// <summary>Troca de intervalo (mensal → anual): crédito do restante antigo + o novo intervalo cheio.</summary>
    public static Proration NewPeriod(long oldPriceCents, long newPriceCents, BillingPeriod oldPeriod, DateTimeOffset now)
    {
        var (remaining, total) = Fraction(oldPeriod, now);
        return new Proration(-new Money(oldPriceCents).Fraction(remaining, total).Cents, newPriceCents);
    }

    private static (long Remaining, long Total) Fraction(BillingPeriod period, DateTimeOffset now)
    {
        var total = period.TotalSeconds;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(total);
        var remaining = Math.Clamp((long)(period.End - now).TotalSeconds, 0, total);
        return (remaining, total);
    }
}
