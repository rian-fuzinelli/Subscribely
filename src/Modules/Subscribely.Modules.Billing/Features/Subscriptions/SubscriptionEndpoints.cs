using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Billing.Domain.Invoices;
using Subscribely.Modules.Billing.Domain.Subscriptions;
using Subscribely.Modules.Billing.Features.Invoices;
using Subscribely.Modules.Billing.Persistence;
using Subscribely.Modules.Payments.Contracts;
using Subscribely.Modules.Tenancy.Contracts;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Billing.Features.Subscriptions;

internal sealed class ChangePlanRequest
{
    [Required]
    [StringLength(30, MinimumLength = 2)]
    public string PlanCode { get; init; } = string.Empty;

    [Required]
    [RegularExpression(IntervalNames.Pattern, ErrorMessage = "Interval must be 'monthly' or 'annual'.")]
    public string Interval { get; init; } = string.Empty;
}

internal sealed record PlanSummary(string Code, string Name, int Version);

internal sealed record PendingChangeResponse(string PlanCode, string Interval, DateTimeOffset EffectiveAt);

internal sealed record SubscriptionResponse(
    string Status,
    PlanSummary Plan,
    string Interval,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    PendingChangeResponse? PendingChange);

internal sealed record ChangePlanResponse(string Effective, Guid? InvoiceId);

internal sealed record AdminSubscriptionResponse(
    Guid Id,
    Guid TenantId,
    string Status,
    string PlanCode,
    string Interval,
    DateTimeOffset CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

internal static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/subscription").WithTags("Subscription");
        group.MapGet("/", GetAsync).RequireTenantMember();
        group.MapPost("/change-plan", ChangePlanAsync).RequireTenantAdmin().WithRequestValidation<ChangePlanRequest>();
        group.MapDelete("/pending-change", CancelPendingChangeAsync).RequireTenantAdmin();
        group.MapPost("/cancel", CancelAsync).RequireTenantAdmin();
        group.MapPost("/resume", ResumeAsync).RequireTenantAdmin();
        group.MapPost("/reactivate", ReactivateAsync).RequireTenantAdmin();

        endpoints.MapGet("/admin/subscriptions", ListForAdminAsync).WithTags("Subscriptions (admin)").RequirePlatformAdmin();
        return endpoints;
    }

    private static async Task<IResult> GetAsync(ITenantContext tenant, BillingDbContext db, CancellationToken ct)
    {
        var response = await ResponseAsync(db, tenant.RequiredTenantId, ct);
        return response is null ? SubscriptionStore.NotFound.ToProblem() : TypedResults.Ok(response);
    }

    private static async Task<IResult> ChangePlanAsync(
        ChangePlanRequest request,
        ITenantContext tenant,
        SubscriptionStore store,
        InvoiceIssuer invoices,
        ITenancyApi tenancy,
        TimeProvider time,
        CancellationToken ct)
    {
        var subscription = await store.FindByTenantAsync(tenant.RequiredTenantId, ct);
        if (subscription is null)
        {
            return SubscriptionStore.NotFound.ToProblem();
        }

        var target = await store.ActivePlanAsync(request.PlanCode, ct);
        if (target is null)
        {
            return SubscriptionStore.PlanUnavailable.ToProblem();
        }

        var now = time.GetUtcNow();
        var current = await store.VersionAsync(subscription.PlanVersionId, ct);
        var changed = subscription.ChangePlan(current, target.CurrentVersion, IntervalNames.Parse(request.Interval), now);
        if (!changed.IsSuccess)
        {
            return changed.Error.ToProblem();
        }

        // Downgrade só pode ser agendado se os assentos ocupados couberem no plano alvo (nunca removemos pessoas).
        if (!changed.Value.Immediate)
        {
            var occupied = await tenancy.CountOccupiedSeatsAsync(subscription.TenantId, ct);
            if (occupied > target.CurrentVersion.MaxSeats)
            {
                return (Error.Conflict("seats-exceed-target-plan", "Remove users or invitations before downgrading.") with
                {
                    Details = new Dictionary<string, object?> { ["activeSeats"] = occupied, ["targetMaxSeats"] = target.CurrentVersion.MaxSeats },
                }).ToProblem();
            }
        }

        var invoice = changed.Value.Billable
            ? await invoices.IssueUpgradeAsync(subscription, changed.Value, current, target.CurrentVersion, now, ct)
            : null;
        if (await store.SaveAsync(subscription, ct) is { } conflict)
        {
            return conflict.ToProblem();
        }

        return TypedResults.Ok(new ChangePlanResponse(changed.Value.Immediate ? "immediate" : "period_end", invoice?.Id));
    }

    private static Task<IResult> CancelPendingChangeAsync(ITenantContext tenant, SubscriptionStore store, BillingDbContext db, TimeProvider time, CancellationToken ct) =>
        MutateAsync(tenant, store, db, s => s.CancelPendingChange(time.GetUtcNow()), ct);

    private static Task<IResult> CancelAsync(ITenantContext tenant, SubscriptionStore store, BillingDbContext db, TimeProvider time, CancellationToken ct) =>
        MutateAsync(tenant, store, db, s => s.Cancel(time.GetUtcNow()), ct);

    private static Task<IResult> ResumeAsync(ITenantContext tenant, SubscriptionStore store, BillingDbContext db, TimeProvider time, CancellationToken ct) =>
        MutateAsync(tenant, store, db, s => s.Resume(time.GetUtcNow()), ct);

    private static async Task<IResult> ReactivateAsync(
        ITenantContext tenant,
        SubscriptionStore store,
        InvoiceIssuer invoices,
        BillingDbContext db,
        IPaymentsApi payments,
        TimeProvider time,
        CancellationToken ct)
    {
        var subscription = await store.FindByTenantAsync(tenant.RequiredTenantId, ct);
        if (subscription is null)
        {
            return SubscriptionStore.NotFound.ToProblem();
        }

        var latest = await store.LatestActiveVersionOfAsync(subscription.PlanVersionId, ct);
        if (latest is null)
        {
            return SubscriptionStore.PlanUnavailable.ToProblem();
        }

        var now = time.GetUtcNow();
        var hasPaymentMethod = await payments.HasDefaultPaymentMethodAsync(subscription.TenantId, ct);
        var reactivated = subscription.Reactivate(hasPaymentMethod, latest.Id, subscription.Interval, now);
        if (!reactivated.IsSuccess)
        {
            return reactivated.Error.ToProblem();
        }

        await invoices.IssueFullPeriodAsync(subscription, InvoiceReason.Reactivation, now, ct);

        return await store.SaveAsync(subscription, ct) is { } conflict
            ? conflict.ToProblem()
            : TypedResults.Ok(await ResponseAsync(db, subscription.TenantId, ct));
    }

    private static async Task<IResult> ListForAdminAsync(string? status, BillingDbContext db, CancellationToken ct)
    {
        var query = db.Subscriptions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<SubscriptionStatus>(status, ignoreCase: true, out var parsed))
            {
                return Error.Validation("status-invalid", $"Unknown status '{status}'.").ToProblem();
            }

            query = query.Where(s => s.Status == parsed);
        }

        var rows = await query
            .OrderBy(s => s.CurrentPeriodEnd)
            .Take(500)
            .Select(s => new
            {
                s.Id,
                s.TenantId,
                s.Status,
                PlanCode = db.Plans.Where(p => p.Id == db.PlanVersions.First(v => v.Id == s.PlanVersionId).PlanId).Select(p => p.Code).First(),
                s.Interval,
                s.CurrentPeriodEnd,
                s.CancelAtPeriodEnd,
            })
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(r => new AdminSubscriptionResponse(
            r.Id, r.TenantId, r.Status.ToString(), r.PlanCode, r.Interval.ToName(), r.CurrentPeriodEnd, r.CancelAtPeriodEnd)).ToList());
    }

    private static async Task<IResult> MutateAsync(
        ITenantContext tenant, SubscriptionStore store, BillingDbContext db, Func<Subscription, Result> action, CancellationToken ct)
    {
        var subscription = await store.FindByTenantAsync(tenant.RequiredTenantId, ct);
        if (subscription is null)
        {
            return SubscriptionStore.NotFound.ToProblem();
        }

        var result = action(subscription);
        if (!result.IsSuccess)
        {
            return result.Error.ToProblem();
        }

        return await store.SaveAsync(subscription, ct) is { } conflict
            ? conflict.ToProblem()
            : TypedResults.Ok(await ResponseAsync(db, subscription.TenantId, ct));
    }

    private static async Task<SubscriptionResponse?> ResponseAsync(BillingDbContext db, Guid tenantId, CancellationToken ct)
    {
        var row = await db.Subscriptions.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => new
            {
                Subscription = s,
                Version = db.PlanVersions.First(v => v.Id == s.PlanVersionId),
                PlanCode = db.Plans.Where(p => p.Id == db.PlanVersions.First(v => v.Id == s.PlanVersionId).PlanId).Select(p => p.Code).First(),
                PendingCode = db.Plans.Where(p => db.PlanVersions.Any(v => v.Id == s.PendingPlanVersionId && v.PlanId == p.Id)).Select(p => p.Code).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return null;
        }

        var s = row.Subscription;
        var pending = row.PendingCode is not null && s.PendingInterval is { } pendingInterval
            ? new PendingChangeResponse(row.PendingCode, pendingInterval.ToName(), s.CurrentPeriodEnd)
            : null;
        return new SubscriptionResponse(
            s.Status.ToString(),
            new PlanSummary(row.PlanCode, row.Version.Name, row.Version.Version),
            s.Interval.ToName(),
            s.TrialEndsAt,
            s.CurrentPeriodStart,
            s.CurrentPeriodEnd,
            s.CancelAtPeriodEnd,
            pending);
    }
}
