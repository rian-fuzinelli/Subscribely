using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Tenancy.Contracts;
using Subscribely.Modules.Tenancy.Persistence;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Tenancy.Features.Tenants;

internal static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/tenant", GetCurrentAsync).WithTags("Tenant").RequireTenantMember();
        endpoints.MapGet("/admin/tenants", ListAsync).WithTags("Tenant (admin)").RequirePlatformAdmin();
        return endpoints;
    }

    private static async Task<IResult> GetCurrentAsync(ITenantContext tenant, TenancyDbContext db, CancellationToken ct)
    {
        var current = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenant.RequiredTenantId)
            .Select(t => new TenantInfo(t.Id, t.Slug, t.Name, t.Status.ToString()))
            .FirstOrDefaultAsync(ct);
        return current is null ? Error.NotFound().ToProblem() : TypedResults.Ok(current);
    }

    private static async Task<IResult> ListAsync(TenancyDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Tenants.AsNoTracking().OrderBy(t => t.CreatedAt)
            .Select(t => new TenantInfo(t.Id, t.Slug, t.Name, t.Status.ToString()))
            .ToListAsync(ct));
}
