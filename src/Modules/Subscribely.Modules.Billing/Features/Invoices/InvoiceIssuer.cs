using Microsoft.EntityFrameworkCore;
using Subscribely.Modules.Billing.Domain.Invoices;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Domain.Subscriptions;
using Subscribely.Modules.Billing.Persistence;

namespace Subscribely.Modules.Billing.Features.Invoices;

/// <summary>
/// Numera e adiciona faturas ao <see cref="BillingDbContext"/>; o <c>SaveChanges</c> de quem chama grava a fatura
/// na mesma transação da transição da assinatura.
/// </summary>
internal sealed class InvoiceIssuer(BillingDbContext db)
{
    private const string NextNumberSql = "SELECT nextval('billing.invoice_number_seq') AS \"Value\"";

    public async Task<Invoice> IssueFullPeriodAsync(Subscription subscription, InvoiceReason reason, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var version = await db.PlanVersions.FirstAsync(v => v.Id == subscription.PlanVersionId, ct);
        var (period, lines) = InvoiceFactory.FullPeriod(subscription, version);
        return await IssueAsync(subscription, reason, period, lines, now, ct);
    }

    public async Task<Invoice> IssueUpgradeAsync(
        Subscription subscription, PlanChange change, PlanVersion previous, PlanVersion current, DateTimeOffset now, CancellationToken ct)
    {
        var (period, lines) = InvoiceFactory.Upgrade(subscription, change, previous, current, now);
        return await IssueAsync(subscription, InvoiceReason.Upgrade, period, lines, now, ct);
    }

    private async Task<Invoice> IssueAsync(
        Subscription subscription, InvoiceReason reason, BillingPeriod period, IReadOnlyList<InvoiceLineDraft> lines, DateTimeOffset now, CancellationToken ct)
    {
        var sequence = await db.Database
            .SqlQueryRaw<long>(NextNumberSql)
            .SingleAsync(ct);
        var invoice = Invoice.Issue(Invoice.FormatNumber(now, sequence), subscription, reason, period, lines, now);
        db.Invoices.Add(invoice);
        return invoice;
    }
}
