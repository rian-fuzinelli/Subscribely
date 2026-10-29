using Microsoft.EntityFrameworkCore;
using Subscribely.Modules.Billing.Contracts;
using Microsoft.Extensions.Logging;
using Subscribely.Modules.Billing.Domain.Invoices;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Features.Subscriptions;
using Subscribely.Modules.Billing.Persistence;

namespace Subscribely.Modules.Billing;

internal sealed partial class BillingApi(BillingDbContext db, EntitlementsCache entitlements, ILogger<BillingApi> logger) : IBillingApi
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

    public async Task<Entitlements?> GetEntitlementsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await entitlements.GetAsync(tenantId, cancellationToken);

    public Task<InvoiceInfo?> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        db.Invoices.AsNoTracking().Where(i => i.Id == invoiceId).Select(ToInfo).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<InvoiceInfo>> ListOpenInvoicesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status == InvoiceStatus.Open)
            .OrderBy(i => i.IssuedAt)
            .Select(ToInfo)
            .ToListAsync(cancellationToken);

    public async Task<MarkInvoicePaidOutcome> MarkInvoicePaidAsync(Guid invoiceId, DateTimeOffset paidAt, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return MarkInvoicePaidOutcome.NotFound;
        }

        var paid = invoice.MarkPaid(paidAt);
        if (!paid.IsSuccess)
        {
            LogPaidAfterVoid(logger, invoiceId);
            return MarkInvoicePaidOutcome.Void;
        }

        if (!paid.Value)
        {
            return MarkInvoicePaidOutcome.AlreadyPaid;
        }

        await db.SaveChangesAsync(cancellationToken);
        return MarkInvoicePaidOutcome.Paid;
    }

    private static readonly System.Linq.Expressions.Expression<Func<Invoice, InvoiceInfo>> ToInfo = i =>
        new InvoiceInfo(i.Id, i.TenantId, i.SubscriptionId, i.Number, i.Status.ToString(), i.TotalCents, i.IssuedAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment received for voided invoice {InvoiceId}; manual reconciliation required")]
    private static partial void LogPaidAfterVoid(ILogger logger, Guid invoiceId);
}
