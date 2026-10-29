using Subscribely.Modules.Billing.Contracts;

namespace Subscribely.Modules.Billing.Domain.Subscriptions;

/// <summary>Nomes públicos dos intervalos (<c>monthly</c>/<c>annual</c>), como no signup.</summary>
internal static class IntervalNames
{
    public const string Pattern = "^(monthly|annual)$";

    public static string ToName(this BillingInterval interval) => interval == BillingInterval.Annual ? "annual" : "monthly";

    public static BillingInterval Parse(string name) => string.Equals(name, "annual", StringComparison.OrdinalIgnoreCase)
        ? BillingInterval.Annual
        : BillingInterval.Monthly;
}
