using System.ComponentModel.DataAnnotations;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Subscribely.Infrastructure.Jobs;
using Subscribely.Infrastructure.Outbox;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Payments.Contracts;
using Subscribely.Modules.Payments.Domain;
using Subscribely.Modules.Payments.Persistence;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Payments.Features.Charges;

internal sealed class SetPaymentMethodRequest
{
    [Required]
    [RegularExpression("^pm_[a-z0-9_]{2,60}$", ErrorMessage = "Token must look like 'pm_...'.")]
    public string Token { get; init; } = string.Empty;
}

internal sealed record PaymentMethodResponse(string Brand, string Last4, DateTimeOffset UpdatedAt);

internal sealed record ChargeResponse(Guid ChargeId, int Attempt, string Status);

internal static class ChargeEndpoints
{
    public static IEndpointRouteBuilder MapChargeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var method = endpoints.MapGroup("/v1/billing/payment-method").WithTags("Payment method").RequireTenantAdmin();
        method.MapGet("/", GetPaymentMethodAsync);
        method.MapPut("/", SetPaymentMethodAsync).WithRequestValidation<SetPaymentMethodRequest>();

        endpoints.MapPost("/v1/invoices/{id:guid}/pay", PayAsync).WithTags("Invoices").RequireTenantAdmin();
        return endpoints;
    }

    private static async Task<IResult> GetPaymentMethodAsync(ITenantContext tenant, PaymentsDbContext db, CancellationToken ct)
    {
        var method = await db.PaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.TenantId == tenant.RequiredTenantId, ct);
        return method is null
            ? Error.NotFound("payment-method-not-found", "No payment method registered.").ToProblem()
            : TypedResults.Ok(new PaymentMethodResponse(method.Brand, method.Last4, method.UpdatedAt));
    }

    /// <summary>Salva o meio padrão; com fatura em aberto e assinatura PastDue/Suspended, tenta cobrar na hora.</summary>
    private static async Task<IResult> SetPaymentMethodAsync(
        SetPaymentMethodRequest request,
        ITenantContext tenant,
        PaymentsDbContext db,
        ChargeService charges,
        IBillingApi billing,
        TimeProvider time,
        CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        var method = await db.PaymentMethods.FirstOrDefaultAsync(m => m.TenantId == tenantId, ct);
        if (method is null)
        {
            db.PaymentMethods.Add(PaymentMethod.Create(tenantId, request.Token, time.GetUtcNow()));
        }
        else
        {
            method.Replace(request.Token, time.GetUtcNow());
        }

        await db.SaveChangesAsync(ct);

        var status = (await billing.GetEntitlementsAsync(tenantId, ct))?.SubscriptionStatus;
        if (status is "PastDue" or "Suspended")
        {
            foreach (var invoice in await billing.ListOpenInvoicesAsync(tenantId, ct))
            {
                await charges.ChargeAsync(invoice, manual: true, ct);
            }
        }

        var saved = await db.PaymentMethods.AsNoTracking().FirstAsync(m => m.TenantId == tenantId, ct);
        return TypedResults.Ok(new PaymentMethodResponse(saved.Brand, saved.Last4, saved.UpdatedAt));
    }

    /// <summary>Nova tentativa com o meio atual; 409 se já houver charge pendente.</summary>
    private static async Task<IResult> PayAsync(Guid id, ITenantContext tenant, IBillingApi billing, ChargeService charges, CancellationToken ct)
    {
        var invoice = await billing.GetInvoiceAsync(id, ct);
        if (invoice is null || invoice.TenantId != tenant.RequiredTenantId)
        {
            return Error.NotFound("invoice-not-found", "Invoice not found.").ToProblem();
        }

        if (invoice.Status != "Open")
        {
            return Error.Conflict("invoice-not-open", "Only open invoices can be paid.").ToProblem();
        }

        var charged = await charges.ChargeAsync(invoice, manual: true, ct);
        return charged.IsSuccess
            ? TypedResults.Accepted((string?)null, new ChargeResponse(charged.Value.Id, charged.Value.Attempt, charged.Value.Status.ToString()))
            : charged.Error.ToProblem();
    }
}

/// <summary>Fatura emitida com total &gt; 0 e meio de pagamento → primeira tentativa de cobrança (idempotente).</summary>
internal sealed class OnInvoiceIssued(PaymentsDbContext db, ChargeService charges, TenantContext tenantContext) : IIntegrationEventHandler<InvoiceIssued>
{
    public async Task HandleAsync(InvoiceIssued @event, CancellationToken cancellationToken)
    {
        if (@event.TotalCents <= 0 || await db.Charges.AnyAsync(c => c.InvoiceId == @event.InvoiceId, cancellationToken))
        {
            return;
        }

        tenantContext.SetSystem(nameof(OnInvoiceIssued), @event.TenantId);
        var invoice = new InvoiceInfo(@event.InvoiceId, @event.TenantId, @event.SubscriptionId, @event.Number, "Open", @event.TotalCents, @event.IssuedAt);

        // Sem meio de pagamento a fatura fica Open, sem cobrança.
        await charges.ChargeAsync(invoice, manual: false, cancellationToken);
    }
}

/// <summary>De hora em hora: retentativas agendadas do dunning (D+1, D+3, D+7 a partir da emissão).</summary>
internal sealed partial class DunningJob(IServiceScopeFactory scopes, TimeProvider time, ILogger<DunningJob> logger)
{
    public const int BatchSize = 100;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 3)]
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        List<Guid> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Dunnings.AsNoTracking()
                .Where(d => d.ExhaustedAt == null && d.ResolvedAt == null && d.NextAttemptAt <= now)
                .OrderBy(d => d.NextAttemptAt).Take(BatchSize).Select(d => d.InvoiceId)
                .ToListAsync(cancellationToken);
        }

        var charged = 0;
        foreach (var invoiceId in due)
        {
            try
            {
                if (await RetryAsync(invoiceId, now, cancellationToken))
                {
                    charged++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRetryFailed(logger, ex, invoiceId);
            }
        }

        return charged;
    }

    private async Task<bool> RetryAsync(Guid invoiceId, DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<PaymentsDbContext>();
        var dunning = await db.Dunnings.FirstAsync(d => d.InvoiceId == invoiceId, ct);
        services.GetRequiredService<TenantContext>().SetSystem(nameof(DunningJob), dunning.TenantId);

        var invoice = await services.GetRequiredService<IBillingApi>().GetInvoiceAsync(invoiceId, ct);
        if (invoice is null || invoice.Status != "Open")
        {
            dunning.Resolve(now);
            await db.SaveChangesAsync(ct);
            return false;
        }

        var charged = await services.GetRequiredService<ChargeService>().ChargeAsync(invoice, manual: false, ct);
        return charged.IsSuccess;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dunning retry for invoice {InvoiceId} failed; retried on the next run")]
    private static partial void LogRetryFailed(ILogger logger, Exception exception, Guid invoiceId);
}

internal sealed class PaymentsRecurringJobs : IRecurringJobsRegistrar
{
    public void Register(IRecurringJobManager manager) =>
        manager.AddOrUpdate<DunningJob>("payments:dunning", j => j.RunAsync(CancellationToken.None), Cron.Hourly(15),
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}
