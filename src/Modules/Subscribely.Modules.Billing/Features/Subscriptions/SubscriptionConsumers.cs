using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Subscribely.Infrastructure.Outbox;
using Subscribely.Infrastructure.Persistence;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Domain.Subscriptions;
using Subscribely.Modules.Billing.Persistence;
using Subscribely.Modules.Payments.Contracts;
using Subscribely.Modules.Tenancy.Contracts;

namespace Subscribely.Modules.Billing.Features.Subscriptions;

/// <summary>Banco do tenant pronto → assinatura <c>Trialing</c> no plano escolhido no signup (idempotente).</summary>
internal sealed class OnTenantProvisioned(
    BillingDbContext db,
    SubscriptionStore store,
    TenantContext tenantContext,
    TimeProvider time) : IIntegrationEventHandler<TenantProvisioned>
{
    public async Task HandleAsync(TenantProvisioned @event, CancellationToken cancellationToken)
    {
        tenantContext.SetSystem(nameof(OnTenantProvisioned), @event.TenantId);
        if (await db.Subscriptions.AnyAsync(s => s.TenantId == @event.TenantId, cancellationToken))
        {
            return;
        }

        // O signup validou o plano; se foi arquivado nesse meio-tempo, o trial usa a última versão mesmo assim.
        var plan = await db.Plans.Include(p => p.Versions).FirstAsync(p => p.Code == @event.PlanCode, cancellationToken);
        var subscription = Subscription.StartTrial(@event.TenantId, plan.CurrentVersion.Id, IntervalNames.Parse(@event.Interval), time.GetUtcNow());
        db.Subscriptions.Add(subscription);

        try
        {
            await store.SaveAsync(subscription, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Entrega duplicada concorrente: a outra execução já criou a assinatura.
        }
    }
}

/// <summary>Fatura paga → <c>PastDue</c>/<c>Suspended</c> voltam a <c>Active</c>.</summary>
internal sealed class OnInvoicePaid(SubscriptionStore store, TenantContext tenantContext, TimeProvider time, ILogger<OnInvoicePaid> logger)
    : IIntegrationEventHandler<InvoicePaid>
{
    public Task HandleAsync(InvoicePaid @event, CancellationToken cancellationToken) =>
        SubscriptionEventHandling.TransitionAsync(store, tenantContext, logger, nameof(OnInvoicePaid), @event.TenantId,
            s => s.SettleDebt(time.GetUtcNow()), cancellationToken);
}

/// <summary>Primeira falha de cobrança → <c>PastDue</c>.</summary>
internal sealed class OnPaymentFailed(SubscriptionStore store, TenantContext tenantContext, TimeProvider time, ILogger<OnPaymentFailed> logger)
    : IIntegrationEventHandler<PaymentFailed>
{
    public Task HandleAsync(PaymentFailed @event, CancellationToken cancellationToken) =>
        SubscriptionEventHandling.TransitionAsync(store, tenantContext, logger, nameof(OnPaymentFailed), @event.TenantId,
            s => s.MarkPastDue(time.GetUtcNow()).IsSuccess, cancellationToken);
}

/// <summary>Retentativas esgotadas → <c>Suspended</c>.</summary>
internal sealed class OnPaymentRetriesExhausted(SubscriptionStore store, TenantContext tenantContext, TimeProvider time, ILogger<OnPaymentRetriesExhausted> logger)
    : IIntegrationEventHandler<PaymentRetriesExhausted>
{
    public Task HandleAsync(PaymentRetriesExhausted @event, CancellationToken cancellationToken) =>
        SubscriptionEventHandling.TransitionAsync(store, tenantContext, logger, nameof(OnPaymentRetriesExhausted), @event.TenantId,
            s => s.Suspend(time.GetUtcNow()).IsSuccess, cancellationToken);
}

internal static partial class SubscriptionEventHandling
{
    /// <summary>
    /// Aplica uma transição vinda de outro módulo. Transição inválida para o status atual = no-op (entrega repetida
    /// ou fora de ordem); conflito de concorrência lança para o outbox tentar de novo.
    /// </summary>
    public static async Task TransitionAsync(
        SubscriptionStore store,
        TenantContext tenantContext,
        ILogger logger,
        string handler,
        Guid tenantId,
        Func<Subscription, bool> transition,
        CancellationToken ct)
    {
        tenantContext.SetSystem(handler, tenantId);
        var subscription = await store.FindByTenantAsync(tenantId, ct);
        if (subscription is null || !transition(subscription))
        {
            LogIgnored(logger, handler, tenantId);
            return;
        }

        if (await store.SaveAsync(subscription, ct) is { } conflict)
        {
            throw new InvalidOperationException($"{handler}: {conflict.Code} for tenant {tenantId}.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Handler} ignored for tenant {TenantId}: no transition from the current status")]
    private static partial void LogIgnored(ILogger logger, string handler, Guid tenantId);
}
