using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Subscribely.Infrastructure.Persistence;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Payments.Domain;
using Subscribely.Modules.Payments.Gateway;
using Subscribely.Modules.Payments.Persistence;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Payments.Features.Charges;

/// <summary>Cria uma tentativa de cobrança e chama o gateway; o resultado chega por webhook.</summary>
internal sealed partial class ChargeService(
    PaymentsDbContext db,
    IPaymentGateway gateway,
    IBillingApi billing,
    IOptions<PaymentsOptions> options,
    TimeProvider time,
    ILogger<ChargeService> logger)
{
    public static readonly Error PaymentMethodRequired =
        Error.PaymentRequired("payment-method-required", "Register a payment method first.");

    public static readonly Error ChargePending =
        Error.Conflict("charge-pending", "A charge for this invoice is already in progress.");

    /// <param name="manual">Pedida pelo tenant (fora da agenda do dunning).</param>
    public async Task<Result<Charge>> ChargeAsync(InvoiceInfo invoice, bool manual, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (!options.Value.ChargesEnabled)
        {
            return Error.Unprocessable("charges-disabled", "Charges are disabled (Payments:Provider = None).");
        }

        var method = await db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.TenantId == invoice.TenantId, ct);
        if (method is null)
        {
            return PaymentMethodRequired;
        }

        var attempts = await db.Charges.Where(c => c.InvoiceId == invoice.InvoiceId).Select(c => new { c.Attempt, c.Status }).ToListAsync(ct);
        if (attempts.Any(a => a.Status == ChargeStatus.Pending))
        {
            return ChargePending;
        }

        var now = time.GetUtcNow();
        var charge = Charge.Start(invoice.InvoiceId, invoice.TenantId, attempts.Count == 0 ? 1 : attempts.Max(a => a.Attempt) + 1, manual, invoice.TotalCents, now);
        db.Charges.Add(charge);
        if (await db.Dunnings.FirstOrDefaultAsync(d => d.InvoiceId == invoice.InvoiceId, ct) is { } dunning)
        {
            dunning.Pause();
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return ChargePending;
        }

        try
        {
            var created = await gateway.CreateChargeAsync(invoice.InvoiceId, invoice.TotalCents, method.GatewayToken, charge.IdempotencyKey, ct);
            charge.AttachGatewayCharge(created.Id);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
            || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            LogGatewayUnavailable(logger, ex, invoice.InvoiceId, charge.Attempt);
            await RegisterFailureAsync(charge, Charge.GatewayUnavailable, time.GetUtcNow(), ct);
            await db.SaveChangesAsync(ct);
        }

        return charge;
    }

    /// <summary>Falha de uma tentativa: abre/avança o dunning (as manuais não consomem a agenda).</summary>
    public async Task RegisterFailureAsync(Charge charge, string failureCode, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(charge);
        charge.Fail(failureCode, now);

        var dunning = await db.Dunnings.FirstOrDefaultAsync(d => d.InvoiceId == charge.InvoiceId, ct);
        if (dunning is null)
        {
            var invoice = await billing.GetInvoiceAsync(charge.InvoiceId, ct);
            dunning = Dunning.Open(charge.InvoiceId, charge.TenantId, invoice?.IssuedAt ?? charge.CreatedAt);
            db.Dunnings.Add(dunning);
            dunning.RegisterScheduledFailure(charge.Attempt, failureCode, now);
            return;
        }

        if (charge.Manual)
        {
            dunning.RegisterManualFailure(charge.Attempt, failureCode, now);
        }
        else
        {
            dunning.RegisterScheduledFailure(charge.Attempt, failureCode, now);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment gateway unavailable for invoice {InvoiceId} (attempt {Attempt})")]
    private static partial void LogGatewayUnavailable(ILogger logger, Exception exception, Guid invoiceId, int attempt);
}
