using System.Globalization;
using Subscribely.Infrastructure.Auditing;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Domain.Subscriptions;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Billing.Domain.Invoices;

internal enum InvoiceReason
{
    TrialConversion,
    Renewal,
    Upgrade,
    Reactivation,
}

internal enum InvoiceStatus
{
    Open,
    Paid,
    Void,
}

/// <summary>Linha a emitir; valor negativo = crédito.</summary>
internal sealed record InvoiceLineDraft(string Description, Guid PlanVersionId, long AmountCents, BillingPeriod Period);

[Auditable]
internal sealed class InvoiceLine
{
    private InvoiceLine()
    {
    }

    public Guid Id { get; private set; }

    public Guid InvoiceId { get; private set; }

    public int Position { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public Guid PlanVersionId { get; private set; }

    public long AmountCents { get; private set; }

    public DateTimeOffset PeriodStart { get; private set; }

    public DateTimeOffset PeriodEnd { get; private set; }

    public static InvoiceLine From(Guid invoiceId, int position, InvoiceLineDraft draft) => new()
    {
        Id = Guid.CreateVersion7(),
        InvoiceId = invoiceId,
        Position = position,
        Description = draft.Description,
        PlanVersionId = draft.PlanVersionId,
        AmountCents = draft.AmountCents,
        PeriodStart = draft.Period.Start,
        PeriodEnd = draft.Period.End,
    };
}

/// <summary>
/// Fatura imutável após a emissão: só o status e os campos de pagamento/anulação mudam.
/// Invariante: <c>TotalCents == Σ linhas</c> e nunca negativo (sem crédito transportado).
/// </summary>
[Auditable]
internal sealed class Invoice : Entity
{
    public const string Currency = Money.Currency;

    private readonly List<InvoiceLine> _lines = [];

    private Invoice()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public Guid TenantId { get; private set; }

    public Guid SubscriptionId { get; private set; }

    public InvoiceReason Reason { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public DateTimeOffset PeriodStart { get; private set; }

    public DateTimeOffset PeriodEnd { get; private set; }

    public long TotalCents { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public string? VoidReason { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public static string FormatNumber(DateTimeOffset issuedAt, long sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"SUB-{issuedAt.UtcDateTime.Year:0000}-{sequence:000000}");

    /// <summary>Emite a fatura. Total ≤ 0 vira <c>Paid</c> de 0 na hora (sem cobrança e sem crédito transportado).</summary>
    public static Invoice Issue(
        string number,
        Subscription subscription,
        InvoiceReason reason,
        BillingPeriod period,
        IReadOnlyList<InvoiceLineDraft> lines,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException("An invoice needs at least one line.", nameof(lines));
        }

        var drafts = lines.ToList();
        var sum = drafts.Sum(l => l.AmountCents);
        if (sum < 0)
        {
            drafts.Add(new InvoiceLineDraft("Crédito não utilizado (não transportado)", drafts[^1].PlanVersionId, -sum, period));
        }

        var invoice = new Invoice
        {
            Number = number,
            TenantId = subscription.TenantId,
            SubscriptionId = subscription.Id,
            Reason = reason,
            Status = InvoiceStatus.Open,
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            TotalCents = Math.Max(sum, 0),
            IssuedAt = now,
        };
        invoice._lines.AddRange(drafts.Select((d, i) => InvoiceLine.From(invoice.Id, i + 1, d)));
        invoice.Raise(new InvoiceIssued(invoice.Id, invoice.TenantId, invoice.SubscriptionId, number, reason.ToString(), invoice.TotalCents, now));

        if (invoice.TotalCents == 0)
        {
            invoice.MarkPaid(now);
        }

        return invoice;
    }

    /// <returns><c>true</c> se mudou para <c>Paid</c>; <c>false</c> se já estava paga (idempotente).</returns>
    public Result<bool> MarkPaid(DateTimeOffset paidAt)
    {
        switch (Status)
        {
            case InvoiceStatus.Paid:
                return false;
            case InvoiceStatus.Void:
                return Error.Conflict("invoice-void", "The invoice was voided; the payment must be reconciled manually.");
            default:
                Status = InvoiceStatus.Paid;
                PaidAt = paidAt;
                Raise(new InvoicePaid(Id, TenantId, SubscriptionId, TotalCents, paidAt));
                return true;
        }
    }

    public Result Void(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("void-reason-required", "A reason is required to void an invoice.");
        }

        switch (Status)
        {
            case InvoiceStatus.Void:
                return Result.Success();
            case InvoiceStatus.Paid:
                return Error.Conflict("invoice-not-open", "Only open invoices can be voided.");
            default:
                Status = InvoiceStatus.Void;
                VoidedAt = now;
                VoidReason = reason.Trim();
                Raise(new InvoiceVoided(Id, TenantId, SubscriptionId, VoidReason, now));
                return Result.Success();
        }
    }
}
