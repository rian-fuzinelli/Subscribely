using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subscribely.Infrastructure.Jobs;
using Subscribely.Infrastructure.Persistence;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Payments.Contracts;
using Subscribely.Modules.Payments.Domain;
using Subscribely.Modules.Payments.Features.Charges;
using Subscribely.Modules.Payments.Gateway;
using Subscribely.Modules.Payments.Persistence;

namespace Subscribely.Modules.Payments.Features.Webhooks;

internal static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/webhooks/payments/{provider}", ReceiveAsync).WithTags("Webhooks").AllowAnonymous();
        return endpoints;
    }

    /// <summary>
    /// Assinatura → insert-first em <c>payment_events</c> → enfileira o processamento → 200. Repetido = 200 sem efeito.
    /// Falha de assinatura = 401 sem detalhes.
    /// </summary>
    private static async Task<IResult> ReceiveAsync(
        string provider,
        HttpRequest request,
        PaymentsDbContext db,
        IBackgroundJobClient jobs,
        IOptions<PaymentsOptions> options,
        TenantContext tenantContext,
        TimeProvider time,
        CancellationToken ct)
    {
        if (!string.Equals(provider, FakeGatewayEndpoints.ProviderName, StringComparison.Ordinal))
        {
            return TypedResults.NotFound();
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);
        var now = time.GetUtcNow();
        if (!WebhookSignature.Verify(options.Value.FakeGateway.WebhookSecret, request.Headers[WebhookSignature.HeaderName], body, now))
        {
            return TypedResults.Unauthorized();
        }

        WebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookPayload>(body, WebhookPayload.Json);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null || string.IsNullOrEmpty(payload.Id) || string.IsNullOrEmpty(payload.Type) || payload.Data is null)
        {
            return TypedResults.BadRequest();
        }

        tenantContext.Set(null, null, ActorType.Webhook, provider);
        var received = PaymentEvent.Receive(provider, payload.Id, payload.Type, body, now);
        db.PaymentEvents.Add(received);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return TypedResults.Ok(); // Duplicado: já recebido, nenhum novo processamento.
        }

        jobs.Enqueue<ProcessPaymentEventJob>(j => j.RunAsync(received.Id, CancellationToken.None));
        return TypedResults.Ok();
    }
}

/// <summary>A charge do webhook ainda não foi gravada: o Hangfire tenta de novo com backoff.</summary>
internal sealed class ChargeNotFoundYetException(string chargeId) : Exception($"Charge {chargeId} not found yet.");

/// <summary>
/// Aplica um webhook à charge, tolerante a ordem e a repetição: charge já terminal → <c>Ignored</c>;
/// valor divergente → <c>Rejected</c>; senão, transição + <c>Processed</c> na mesma transação.
/// </summary>
internal sealed partial class ProcessPaymentEventJob(
    PaymentsDbContext db,
    ChargeService charges,
    IBillingApi billing,
    TenantContext tenantContext,
    TimeProvider time,
    ILogger<ProcessPaymentEventJob> logger)
{
    [Queue(JobQueues.Critical)]
    [AutomaticRetry(Attempts = 5, DelaysInSeconds = [1, 2, 4, 8, 16])]
    public async Task RunAsync(Guid paymentEventId, CancellationToken cancellationToken)
    {
        var received = await db.PaymentEvents.FirstOrDefaultAsync(e => e.Id == paymentEventId, cancellationToken);
        if (received is null || received.Status != PaymentEventStatus.Received)
        {
            return;
        }

        var payload = JsonSerializer.Deserialize<WebhookPayload>(received.Payload, WebhookPayload.Json)!;
        var data = payload.Data;
        var charge = await db.Charges.FirstOrDefaultAsync(c => c.GatewayChargeId == data.ChargeId, cancellationToken)
            ?? await db.Charges.FirstOrDefaultAsync(
                c => c.InvoiceId == data.InvoiceId && c.GatewayChargeId == null && c.Status == ChargeStatus.Pending, cancellationToken)
            ?? throw new ChargeNotFoundYetException(data.ChargeId);

        tenantContext.Set(charge.TenantId, null, ActorType.Webhook, received.Provider);
        var now = time.GetUtcNow();

        if (charge.IsTerminal)
        {
            LogIgnored(logger, received.ProviderEventId, charge.Id, charge.Status.ToString());
            received.Complete(PaymentEventStatus.Ignored, now, $"charge-already-{charge.Status.ToString().ToLowerInvariant()}");
        }
        else if (data.AmountCents != charge.AmountCents)
        {
            LogAmountMismatch(logger, received.ProviderEventId, charge.Id, data.AmountCents, charge.AmountCents);
            received.Complete(PaymentEventStatus.Rejected, now, "amount-mismatch");
        }
        else if (payload.Type == WebhookPayload.Succeeded)
        {
            await SucceedAsync(charge, now, cancellationToken);
            received.Complete(PaymentEventStatus.Processed, now);
        }
        else if (payload.Type == WebhookPayload.Failed)
        {
            await charges.RegisterFailureAsync(charge, data.FailureCode ?? "unknown", now, cancellationToken);
            received.Complete(PaymentEventStatus.Processed, now);
        }
        else
        {
            received.Complete(PaymentEventStatus.Ignored, now, "unknown-type");
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SucceedAsync(Charge charge, DateTimeOffset now, CancellationToken ct)
    {
        charge.Succeed(now);
        if (await billing.MarkInvoicePaidAsync(charge.InvoiceId, now, ct) == MarkInvoicePaidOutcome.Void)
        {
            LogPaidVoidInvoice(logger, charge.InvoiceId, charge.Id);
        }

        if (await db.Dunnings.FirstOrDefaultAsync(d => d.InvoiceId == charge.InvoiceId, ct) is { } dunning)
        {
            dunning.Resolve(now);
        }

        db.Publish(new PaymentSucceeded(charge.TenantId, charge.InvoiceId, charge.Id, charge.AmountCents, now));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment event {EventId} ignored: charge {ChargeId} is already {Status}")]
    private static partial void LogIgnored(ILogger logger, string eventId, Guid chargeId, string status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment event {EventId} rejected: amount {EventAmount} differs from charge {ChargeId} amount {ChargeAmount}")]
    private static partial void LogAmountMismatch(ILogger logger, string eventId, Guid chargeId, long eventAmount, long chargeAmount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Charge {ChargeId} succeeded for voided invoice {InvoiceId}; manual reconciliation required")]
    private static partial void LogPaidVoidInvoice(ILogger logger, Guid invoiceId, Guid chargeId);
}
