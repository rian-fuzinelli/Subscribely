using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Persistence;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Persistence;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Billing.Features.Plans;

internal sealed record PlanResponse(
    string Code,
    string Name,
    string Status,
    int Version,
    long MonthlyPriceCents,
    long AnnualPriceCents,
    int RequestQuotaMonthly,
    int MaxSeats)
{
    public static PlanResponse From(Plan plan)
    {
        var v = plan.CurrentVersion;
        return new PlanResponse(plan.Code, v.Name, plan.Status.ToString(), v.Version, v.MonthlyPriceCents, v.AnnualPriceCents, v.RequestQuotaMonthly, v.MaxSeats);
    }
}

internal class PlanTermsRequest
{
    [Required]
    [StringLength(60, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Range(0, long.MaxValue / 12)]
    public long MonthlyPriceCents { get; init; }

    [Range(0, long.MaxValue)]
    public long AnnualPriceCents { get; init; }

    [Range(1, int.MaxValue)]
    public int RequestQuotaMonthly { get; init; }

    [Range(1, 100_000)]
    public int MaxSeats { get; init; }

    public PlanTerms ToTerms() => new(Name, MonthlyPriceCents, AnnualPriceCents, RequestQuotaMonthly, MaxSeats);
}

internal sealed class CreatePlanRequest : PlanTermsRequest
{
    [Required]
    [RegularExpression("^[a-z0-9-]{2,30}$", ErrorMessage = "Code must have 2 to 30 lowercase letters, digits or hyphens.")]
    public string Code { get; init; } = string.Empty;
}

internal sealed class UpdatePlanRequest : PlanTermsRequest;

internal static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/plans").WithTags("Plans (admin)").RequirePlatformAdmin();
        admin.MapGet("/", ListAllAsync);
        admin.MapPost("/", CreateAsync).WithRequestValidation<CreatePlanRequest>();
        admin.MapPut("/{code}", UpdateAsync).WithRequestValidation<UpdatePlanRequest>();
        admin.MapPost("/{code}/archive", ArchiveAsync);

        endpoints.MapGet("/v1/plans", ListActiveAsync).WithTags("Plans").AllowAnonymous();
        return endpoints;
    }

    private static async Task<Ok<List<PlanResponse>>> ListActiveAsync(BillingDbContext db, CancellationToken ct)
    {
        var plans = await db.Plans.AsNoTracking().Include(p => p.Versions)
            .Where(p => p.Status == PlanStatus.Active)
            .OrderBy(p => p.Code)
            .ToListAsync(ct);
        return TypedResults.Ok(plans.Select(PlanResponse.From).OrderBy(p => p.MonthlyPriceCents).ToList());
    }

    private static async Task<Ok<List<PlanResponse>>> ListAllAsync(BillingDbContext db, CancellationToken ct)
    {
        var plans = await db.Plans.AsNoTracking().Include(p => p.Versions).OrderBy(p => p.Code).ToListAsync(ct);
        return TypedResults.Ok(plans.Select(PlanResponse.From).ToList());
    }

    private static async Task<Results<Created<PlanResponse>, ProblemHttpResult>> CreateAsync(
        CreatePlanRequest request, BillingDbContext db, TimeProvider time, CancellationToken ct)
    {
        var created = Plan.Create(request.Code, request.ToTerms(), time.GetUtcNow());
        if (!created.IsSuccess)
        {
            return created.Error.ToProblem();
        }

        db.Plans.Add(created.Value);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("plan-code-taken", $"Plan code '{request.Code}' already exists.").ToProblem();
        }

        return TypedResults.Created($"/admin/plans/{created.Value.Code}", PlanResponse.From(created.Value));
    }

    private static async Task<Results<Ok<PlanResponse>, ProblemHttpResult>> UpdateAsync(
        string code, UpdatePlanRequest request, BillingDbContext db, TimeProvider time, CancellationToken ct)
    {
        var plan = await db.Plans.Include(p => p.Versions).FirstOrDefaultAsync(p => p.Code == code, ct);
        if (plan is null)
        {
            return Error.NotFound("plan-not-found", $"Plan '{code}' was not found.").ToProblem();
        }

        var revised = plan.Revise(request.ToTerms(), time.GetUtcNow());
        if (!revised.IsSuccess)
        {
            return revised.Error.ToProblem();
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("concurrency-conflict", "The plan was changed concurrently; retry.").ToProblem();
        }

        return TypedResults.Ok(PlanResponse.From(plan));
    }

    private static async Task<Results<Ok<PlanResponse>, ProblemHttpResult>> ArchiveAsync(
        string code, BillingDbContext db, CancellationToken ct)
    {
        var plan = await db.Plans.Include(p => p.Versions).FirstOrDefaultAsync(p => p.Code == code, ct);
        if (plan is null)
        {
            return Error.NotFound("plan-not-found", $"Plan '{code}' was not found.").ToProblem();
        }

        plan.Archive();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(PlanResponse.From(plan));
    }
}
