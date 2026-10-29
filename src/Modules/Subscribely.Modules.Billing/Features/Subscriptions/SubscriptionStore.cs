using Microsoft.EntityFrameworkCore;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Domain.Subscriptions;
using Subscribely.Modules.Billing.Persistence;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Billing.Features.Subscriptions;

/// <summary>Carrega e grava a assinatura de um tenant: concorrência otimista → 409 e invalidação do cache de limites.</summary>
internal sealed class SubscriptionStore(BillingDbContext db, EntitlementsCache entitlements)
{
    public static readonly Error NotFound = Error.NotFound("subscription-not-found", "The tenant has no subscription.");

    public static readonly Error PlanUnavailable = Error.Unprocessable("plan-unavailable", "The plan is not available.");

    public Task<Subscription?> FindByTenantAsync(Guid tenantId, CancellationToken ct) =>
        db.Subscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public Task<Subscription?> FindAsync(Guid subscriptionId, CancellationToken ct) =>
        db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct);

    public Task<PlanVersion> VersionAsync(Guid planVersionId, CancellationToken ct) =>
        db.PlanVersions.FirstAsync(v => v.Id == planVersionId, ct);

    /// <summary>Plano ativo com as versões; <c>null</c> se não existir ou estiver arquivado.</summary>
    public Task<Plan?> ActivePlanAsync(string code, CancellationToken ct) =>
        db.Plans.Include(p => p.Versions).FirstOrDefaultAsync(p => p.Code == code && p.Status == PlanStatus.Active, ct);

    /// <summary>Versão vigente do plano da versão informada; <c>null</c> se o plano foi arquivado.</summary>
    public async Task<PlanVersion?> LatestActiveVersionOfAsync(Guid planVersionId, CancellationToken ct)
    {
        var planId = await db.PlanVersions.Where(v => v.Id == planVersionId).Select(v => v.PlanId).FirstAsync(ct);
        var plan = await db.Plans.Include(p => p.Versions).FirstAsync(p => p.Id == planId, ct);
        return plan.Status == PlanStatus.Active ? plan.CurrentVersion : null;
    }

    public async Task<Error?> SaveAsync(Subscription subscription, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("concurrency-conflict", "The subscription was changed concurrently; retry.");
        }

        await entitlements.InvalidateAsync(subscription.TenantId, ct);
        return null;
    }
}
