using System.Linq.Expressions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Subscribely.Infrastructure.Jobs;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Billing.Domain.Invoices;
using Subscribely.Modules.Billing.Domain.Subscriptions;
using Subscribely.Modules.Billing.Features.Invoices;
using Subscribely.Modules.Billing.Persistence;
using Subscribely.Modules.Payments.Contracts;

namespace Subscribely.Modules.Billing.Features.Subscriptions.Jobs;

/// <summary>Escopo de DI de um item do lote: a assinatura já carregada e os serviços para agir sobre ela.</summary>
internal sealed record SubscriptionStep(Subscription Subscription, IServiceProvider Services, DateTimeOffset Now);

/// <summary>
/// Percorre as assinaturas elegíveis em lotes de 100, uma transação (e um escopo de DI) por assinatura:
/// a falha de uma não derruba o lote. Uma assinatura que não muda sai da rodada (sem laço infinito).
/// </summary>
internal sealed partial class SubscriptionBatchRunner(IServiceScopeFactory scopes, TimeProvider time, ILogger<SubscriptionBatchRunner> logger)
{
    public const int BatchSize = 100;

    /// <returns>Quantidade de assinaturas alteradas.</returns>
    public async Task<int> RunAsync(
        string jobName,
        Func<DateTimeOffset, Expression<Func<Subscription, bool>>> eligible,
        Func<SubscriptionStep, Task<bool>> step,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var filter = eligible(now);
        var skipped = new HashSet<Guid>();
        var changed = 0;

        while (true)
        {
            List<Guid> ids;
            var skippedIds = skipped.ToArray();
            await using (var scope = scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                ids = await db.Subscriptions.AsNoTracking().Where(filter)
                    .Where(s => !skippedIds.Contains(s.Id))
                    .OrderBy(s => s.Id).Take(BatchSize).Select(s => s.Id)
                    .ToListAsync(ct);
            }

            if (ids.Count == 0)
            {
                return changed;
            }

            foreach (var id in ids)
            {
                if (await RunOneAsync(jobName, id, now, step, ct))
                {
                    changed++;
                }
                else
                {
                    skipped.Add(id);
                }
            }
        }
    }

    private async Task<bool> RunOneAsync(string jobName, Guid id, DateTimeOffset now, Func<SubscriptionStep, Task<bool>> step, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<SubscriptionStore>();
        try
        {
            var subscription = await store.FindAsync(id, ct);
            if (subscription is null)
            {
                return false;
            }

            scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystem(jobName, subscription.TenantId);
            if (!await step(new SubscriptionStep(subscription, scope.ServiceProvider, now)))
            {
                return false;
            }

            if (await store.SaveAsync(subscription, ct) is { } conflict)
            {
                LogItemFailed(logger, null, jobName, id, conflict.Code);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogItemFailed(logger, ex, jobName, id, ex.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Job} skipped subscription {SubscriptionId}: {Reason}; retried on the next run")]
    private static partial void LogItemFailed(ILogger logger, Exception? exception, string job, Guid subscriptionId, string reason);
}

/// <summary>Diário: aviso de D-3 do fim do trial, uma única vez por assinatura.</summary>
internal sealed class TrialEndingNotifierJob(SubscriptionBatchRunner runner)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 3)]
    public Task<int> RunAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(
            nameof(TrialEndingNotifierJob),
            now => s => s.Status == SubscriptionStatus.Trialing && s.TrialEndingNotifiedAt == null
                && s.TrialEndsAt > now && s.TrialEndsAt <= now + Subscription.TrialEndingNotice,
            step => Task.FromResult(step.Subscription.NotifyTrialEnding(step.Now)),
            cancellationToken);
}

/// <summary>De hora em hora: trial vencido vira <c>Active</c> (com meio de pagamento), <c>Expired</c> ou <c>Canceled</c>.</summary>
internal sealed class TrialExpiryJob(SubscriptionBatchRunner runner)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 3)]
    public Task<int> RunAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(
            nameof(TrialExpiryJob),
            now => s => s.Status == SubscriptionStatus.Trialing && s.TrialEndsAt <= now,
            async step =>
            {
                var payments = step.Services.GetRequiredService<IPaymentsApi>();
                var hasPaymentMethod = await payments.HasDefaultPaymentMethodAsync(step.Subscription.TenantId, cancellationToken);
                var ended = step.Subscription.EndTrial(hasPaymentMethod, step.Now);
                if (ended.IsSuccess && ended.Value == SubscriptionStatus.Active)
                {
                    await step.Services.GetRequiredService<InvoiceIssuer>()
                        .IssueFullPeriodAsync(step.Subscription, InvoiceReason.TrialConversion, step.Now, cancellationToken);
                }

                return ended.IsSuccess;
            },
            cancellationToken);
}

/// <summary>De hora em hora: fecha o período (cancelamento → downgrade → migração de versão) e renova.</summary>
internal sealed class PeriodEndJob(SubscriptionBatchRunner runner)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 3)]
    public Task<int> RunAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(
            nameof(PeriodEndJob),
            now => s => (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue) && s.CurrentPeriodEnd <= now,
            async step =>
            {
                var store = step.Services.GetRequiredService<SubscriptionStore>();
                var latest = await store.LatestActiveVersionOfAsync(step.Subscription.PlanVersionId, cancellationToken);
                var closure = step.Subscription.ClosePeriod(step.Now, latest?.Id);
                if (closure == PeriodClosure.Renewed)
                {
                    await step.Services.GetRequiredService<InvoiceIssuer>()
                        .IssueFullPeriodAsync(step.Subscription, InvoiceReason.Renewal, step.Now, cancellationToken);
                }

                return closure is not null;
            },
            cancellationToken);
}

/// <summary>Diário: suspensa há 30 dias → <c>Canceled</c>, e a fatura em aberto é anulada (<c>suspension-expired</c>).</summary>
internal sealed class SuspensionExpiryJob(SubscriptionBatchRunner runner)
{
    public const string VoidReason = "suspension-expired";

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 3)]
    public Task<int> RunAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(
            nameof(SuspensionExpiryJob),
            now => s => s.Status == SubscriptionStatus.Suspended && s.SuspendedAt <= now - Subscription.SuspensionLimit,
            async step =>
            {
                if (!step.Subscription.ExpireSuspension(step.Now))
                {
                    return false;
                }

                var db = step.Services.GetRequiredService<BillingDbContext>();
                var open = await db.Invoices
                    .Where(i => i.SubscriptionId == step.Subscription.Id && i.Status == InvoiceStatus.Open)
                    .ToListAsync(cancellationToken);
                open.ForEach(i => i.Void(VoidReason, step.Now));
                return true;
            },
            cancellationToken);
}

internal sealed class BillingRecurringJobs : IRecurringJobsRegistrar
{
    public void Register(IRecurringJobManager manager)
    {
        var utc = new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc };
        manager.AddOrUpdate<TrialEndingNotifierJob>("billing:trial-ending-notifier", j => j.RunAsync(CancellationToken.None), Cron.Daily(9), utc);
        manager.AddOrUpdate<TrialExpiryJob>("billing:trial-expiry", j => j.RunAsync(CancellationToken.None), Cron.Hourly(), utc);
        manager.AddOrUpdate<PeriodEndJob>("billing:period-end", j => j.RunAsync(CancellationToken.None), Cron.Hourly(5), utc);
        manager.AddOrUpdate<SuspensionExpiryJob>("billing:suspension-expiry", j => j.RunAsync(CancellationToken.None), Cron.Daily(3), utc);
    }
}
