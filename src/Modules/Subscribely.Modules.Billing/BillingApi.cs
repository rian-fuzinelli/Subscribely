using Microsoft.EntityFrameworkCore;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Persistence;

namespace Subscribely.Modules.Billing;

internal sealed class BillingApi(BillingDbContext db) : IBillingApi
{
    public async Task<PlanVersionInfo?> GetActivePlanVersionAsync(string planCode, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.AsNoTracking().Include(p => p.Versions)
            .FirstOrDefaultAsync(p => p.Code == planCode && p.Status == PlanStatus.Active, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var v = plan.CurrentVersion;
        return new PlanVersionInfo(v.Id, plan.Code, v.Name, v.Version, v.MonthlyPriceCents, v.AnnualPriceCents, v.RequestQuotaMonthly, v.MaxSeats);
    }
}
