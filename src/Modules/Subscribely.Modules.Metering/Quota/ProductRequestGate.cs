using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Metering.Contracts;
using Subscribely.Modules.Metering.Persistence;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Metering.Quota;

internal sealed class MeteringOptions
{
    public const string Section = "Metering";

    /// <summary>Desliga o enforcement (rollback rápido); os endpoints de produto seguem autenticados por API key.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Gate dos endpoints de produto, nesta ordem: status da assinatura (402, sem consumir quota) → quota (429 com
/// <c>Retry-After</c>) → handler. A requisição liberada conta, qualquer que seja o status do handler.
/// </summary>
internal sealed class ProductRequestGate(
    ITenantContext tenant,
    IBillingApi billing,
    QuotaCounter counter,
    QuotaAlerts alerts,
    IOptions<MeteringOptions> options,
    TimeProvider time) : IProductRequestGate
{
    public const string LimitHeader = "X-RateLimit-Limit";
    public const string RemainingHeader = "X-RateLimit-Remaining";
    public const string ResetHeader = "X-RateLimit-Reset";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (!options.Value.Enabled)
        {
            return await next(context);
        }

        var http = context.HttpContext;
        var tenantId = tenant.RequiredTenantId;
        var entitlements = await billing.GetEntitlementsAsync(tenantId, http.RequestAborted);
        if (entitlements is not { ProductAccessAllowed: true })
        {
            return Error.PaymentRequired("subscription-inactive", "The subscription is not active; reactivate it to use the product.").ToProblem();
        }

        var now = time.GetUtcNow();
        var decision = await counter.TryConsumeAsync(tenantId, entitlements.RequestQuotaMonthly, now);
        var headers = http.Response.Headers;
        headers[LimitHeader] = decision.Limit.ToString(CultureInfo.InvariantCulture);
        headers[RemainingHeader] = decision.Remaining.ToString(CultureInfo.InvariantCulture);
        headers[ResetHeader] = decision.Period.ResetsAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        if (!decision.Allowed)
        {
            headers.RetryAfter = decision.Period.SecondsUntilReset(now).ToString(CultureInfo.InvariantCulture);
            return Error.TooManyRequests("quota-exceeded", "The monthly request quota has been reached.").ToProblem();
        }

        if (decision.Used is { } used)
        {
            await alerts.NotifyCrossedAsync(tenantId, decision.Period, used, decision.Limit, http.RequestAborted);
        }

        return await next(context);
    }
}

/// <summary>Alertas de 80% e 100%: <c>SET NX</c> no Redis garante um único evento por threshold e período.</summary>
internal sealed class QuotaAlerts(QuotaCounter counter, MeteringDbContext db, TimeProvider time)
{
    public static readonly int[] Thresholds = [80, 100];

    /// <summary><c>ceil(limite × threshold / 100)</c> em inteiros.</summary>
    public static long UsageFor(int threshold, long limit) => ((limit * threshold) + 99) / 100;

    public async Task NotifyCrossedAsync(Guid tenantId, QuotaPeriod period, long used, long limit, CancellationToken ct)
    {
        var published = false;
        foreach (var threshold in Thresholds)
        {
            if (used >= UsageFor(threshold, limit) && await counter.TryClaimAlertAsync(tenantId, period, threshold))
            {
                db.Publish(new QuotaThresholdReached(tenantId, period.Name, threshold, limit, time.GetUtcNow()));
                published = true;
            }
        }

        if (published)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
