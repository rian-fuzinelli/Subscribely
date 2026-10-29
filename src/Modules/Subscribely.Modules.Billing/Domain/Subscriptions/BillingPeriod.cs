using Subscribely.Modules.Billing.Contracts;

namespace Subscribely.Modules.Billing.Domain.Subscriptions;

/// <summary>
/// Período de cobrança <c>[Start, End)</c>. As fronteiras são sempre <c>âncora + k intervalos</c>, então o dia
/// original é preservado: âncora em 31/01 → 28/02 → 31/03.
/// </summary>
internal readonly record struct BillingPeriod(DateTimeOffset Start, DateTimeOffset End)
{
    public long TotalSeconds => (long)(End - Start).TotalSeconds;

    /// <summary>Primeiro período que começa na âncora.</summary>
    public static BillingPeriod Starting(DateTimeOffset anchor, BillingInterval interval) =>
        new(anchor, Add(anchor, interval, 1));

    /// <summary>Período seguinte ao que termina em <paramref name="end"/>, mantendo a âncora.</summary>
    public static BillingPeriod After(DateTimeOffset anchor, BillingInterval interval, DateTimeOffset end)
    {
        var periods = 1;
        while (Add(anchor, interval, periods) <= end)
        {
            periods++;
        }

        return new BillingPeriod(end, Add(anchor, interval, periods));
    }

    public static DateTimeOffset Add(DateTimeOffset anchor, BillingInterval interval, int periods) => interval switch
    {
        BillingInterval.Monthly => anchor.AddMonths(periods),
        BillingInterval.Annual => anchor.AddYears(periods),
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, null),
    };
}
