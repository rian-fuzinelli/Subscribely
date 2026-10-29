using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Persistence;

namespace Subscribely.Modules.Billing.Features.Subscriptions;

/// <summary>
/// Limites vigentes por tenant (HybridCache: memória + Redis). Toda escrita na assinatura chama
/// <see cref="InvalidateAsync"/> logo após o <c>SaveChanges</c>.
/// </summary>
internal sealed class EntitlementsCache(HybridCache cache, BillingDbContext db)
{
    private static readonly HybridCacheEntryOptions Options = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1),
    };

    public ValueTask<Entitlements?> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            Key(tenantId),
            (db, tenantId),
            static (state, ct) => new ValueTask<Entitlements?>(LoadAsync(state.db, state.tenantId, ct)),
            Options,
            [Tag(tenantId)],
            cancellationToken);

    public ValueTask InvalidateAsync(Guid tenantId, CancellationToken cancellationToken) =>
        cache.RemoveByTagAsync(Tag(tenantId), cancellationToken);

    internal static async Task<Entitlements?> LoadAsync(BillingDbContext db, Guid tenantId, CancellationToken ct)
    {
        var row = await db.Subscriptions.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => new
            {
                s.Status,
                Current = db.PlanVersions.First(v => v.Id == s.PlanVersionId),
                PendingSeats = db.PlanVersions.Where(v => v.Id == s.PendingPlanVersionId).Select(v => (int?)v.MaxSeats).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return null;
        }

        var maxSeats = row.PendingSeats is { } pending ? Math.Min(pending, row.Current.MaxSeats) : row.Current.MaxSeats;
        var access = row.Status is Domain.Subscriptions.SubscriptionStatus.Trialing
            or Domain.Subscriptions.SubscriptionStatus.Active
            or Domain.Subscriptions.SubscriptionStatus.PastDue;
        return new Entitlements(row.Status.ToString(), row.Current.RequestQuotaMonthly, maxSeats, access);
    }

    private static string Key(Guid tenantId) => $"billing:entitlements:{tenantId:N}";

    private static string Tag(Guid tenantId) => $"tenant:{tenantId:N}";
}
