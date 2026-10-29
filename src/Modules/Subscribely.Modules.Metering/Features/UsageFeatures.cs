using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Subscribely.Infrastructure.Jobs;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Metering.Persistence;
using Subscribely.Modules.Metering.Quota;
using Subscribely.Modules.Tenancy.Contracts;

namespace Subscribely.Modules.Metering.Features;

internal sealed record UsageResponse(string Period, long RequestsUsed, long RequestLimit, int SeatsUsed, int SeatLimit);

internal sealed record UsageHistoryItem(string Period, long RequestsCount, DateTimeOffset UpdatedAt);

internal static class UsageEndpoints
{
    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/usage").WithTags("Usage").RequireTenantMember();
        group.MapGet("/", GetAsync);
        group.MapGet("/history", HistoryAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        ITenantContext tenant,
        QuotaCounter counter,
        IBillingApi billing,
        ITenancyApi tenancy,
        MeteringDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        var period = QuotaPeriod.At(time.GetUtcNow());
        var entitlements = await billing.GetEntitlementsAsync(tenantId, ct);

        // Redis fora: devolve o último snapshot em vez de falhar.
        var used = await counter.CurrentAsync(tenantId, period)
            ?? await db.UsagePeriods.Where(u => u.TenantId == tenantId && u.Period == period.Name).Select(u => u.RequestsCount).FirstOrDefaultAsync(ct);

        return TypedResults.Ok(new UsageResponse(
            period.Name,
            used,
            entitlements?.RequestQuotaMonthly ?? 0,
            await tenancy.CountOccupiedSeatsAsync(tenantId, ct),
            entitlements?.MaxSeats ?? 0));
    }

    private static async Task<IResult> HistoryAsync(ITenantContext tenant, MeteringDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.UsagePeriods.AsNoTracking()
            .Where(u => u.TenantId == tenant.RequiredTenantId)
            .OrderByDescending(u => u.Period)
            .Take(12)
            .Select(u => new UsageHistoryItem(u.Period, u.RequestsCount, u.UpdatedAt))
            .ToListAsync(ct));
}

/// <summary>
/// De hora em hora: copia os contadores do Redis (mês atual e anterior) para <c>metering.usage_periods</c>.
/// Idempotente; nunca diminui um valor já gravado (uma chave perdida no Redis não apaga o histórico).
/// </summary>
internal sealed class UsageSnapshotJob(IConnectionMultiplexer redis, MeteringDbContext db, TimeProvider time)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 3)]
    [Queue(JobQueues.Low)]
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var current = QuotaPeriod.At(now);
        var previous = QuotaPeriod.At(current.ResetsAt.AddMonths(-2));
        var written = 0;

        foreach (var period in new[] { previous, current })
        {
            var pattern = period.CounterKey(Guid.Empty).Replace(Guid.Empty.ToString(), "*", StringComparison.Ordinal);
            foreach (var server in redis.GetServers().Where(s => !s.IsReplica))
            {
                await foreach (var key in server.KeysAsync(pattern: pattern, pageSize: 500).WithCancellation(cancellationToken))
                {
                    if (!Guid.TryParse(key.ToString().Split(':')[1], out var tenantId))
                    {
                        continue;
                    }

                    var count = (long)await redis.GetDatabase().StringGetAsync(key);
                    await db.Database.ExecuteSqlAsync($"""
                        INSERT INTO metering.usage_periods (tenant_id, period, requests_count, updated_at)
                        VALUES ({tenantId}, {period.Name}, {count}, {now})
                        ON CONFLICT (tenant_id, period) DO UPDATE
                        SET requests_count = GREATEST(usage_periods.requests_count, EXCLUDED.requests_count),
                            updated_at = EXCLUDED.updated_at
                        """, cancellationToken);
                    written++;
                }
            }
        }

        return written;
    }
}

internal sealed class MeteringRecurringJobs : IRecurringJobsRegistrar
{
    public void Register(IRecurringJobManager manager) =>
        manager.AddOrUpdate<UsageSnapshotJob>("metering:usage-snapshot", j => j.RunAsync(CancellationToken.None), Cron.Hourly(30),
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}
