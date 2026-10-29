using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Auditing;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Audit.Contracts;
using Subscribely.Modules.Audit.Persistence;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Audit.Features.QueryAudit;

internal sealed record AuditEntryResponse(
    Guid Id,
    Guid? TenantId,
    DateTimeOffset OccurredAt,
    string ActorType,
    string? ActorId,
    string Action,
    string? EntityType,
    string? EntityId,
    string? Before,
    string? After,
    string? CorrelationId);

internal sealed record AuditPage(IReadOnlyList<AuditEntryResponse> Items, string? NextCursor);

internal static class AuditEndpoints
{
    private const int PageSize = 50;

    public static IEndpointRouteBuilder MapAuditQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/audit", QueryTenantAsync).WithTags("Audit").RequireTenantAdmin();
        endpoints.MapGet("/admin/audit", QueryAdminAsync).WithTags("Audit (admin)").RequirePlatformAdmin();
        return endpoints;
    }

    private static async Task<Results<Ok<AuditPage>, ProblemHttpResult>> QueryTenantAsync(
        ITenantContext tenant,
        AuditDbContext db,
        IServiceProvider services,
        string? source,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? action,
        string? cursor,
        CancellationToken ct)
    {
        IQueryable<AuditEntry> query;
        switch (source ?? "control")
        {
            case "control":
                var tenantId = tenant.RequiredTenantId;
                query = db.Entries.AsNoTracking().Where(e => e.TenantId == tenantId);
                break;
            case "product":
                if (services.GetService(typeof(IProductAuditSource)) is not IProductAuditSource product)
                {
                    return Error.Validation("audit-source-unavailable", "Product audit is not available.").ToProblem();
                }

                query = product.Entries.AsNoTracking();
                break;
            default:
                return Error.Validation("audit-source-invalid", "source must be 'control' or 'product'.").ToProblem();
        }

        return await PageAsync(query, from, to, action, cursor, ct);
    }

    private static Task<Results<Ok<AuditPage>, ProblemHttpResult>> QueryAdminAsync(
        AuditDbContext db,
        Guid? tenantId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? action,
        string? cursor,
        CancellationToken ct)
    {
        var query = db.Entries.AsNoTracking();
        if (tenantId is not null)
        {
            query = query.Where(e => e.TenantId == tenantId);
        }

        return PageAsync(query, from, to, action, cursor, ct);
    }

    private static async Task<Results<Ok<AuditPage>, ProblemHttpResult>> PageAsync(
        IQueryable<AuditEntry> query, DateTimeOffset? from, DateTimeOffset? to, string? action, string? cursor, CancellationToken ct)
    {
        if (from is not null)
        {
            query = query.Where(e => e.OccurredAt >= from);
        }

        if (to is not null)
        {
            query = query.Where(e => e.OccurredAt < to);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(e => e.Action == action);
        }

        if (cursor is not null)
        {
            if (!TryDecodeCursor(cursor, out var cursorAt, out var cursorId))
            {
                return Error.Validation("cursor-invalid", "Invalid cursor.").ToProblem();
            }

            query = query.Where(e => e.OccurredAt < cursorAt || (e.OccurredAt == cursorAt && e.Id.CompareTo(cursorId) < 0));
        }

        var items = await query
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Take(PageSize + 1)
            .Select(e => new AuditEntryResponse(e.Id, e.TenantId, e.OccurredAt, e.ActorType.ToString(), e.ActorId, e.Action, e.EntityType, e.EntityId, e.Before, e.After, e.CorrelationId))
            .ToListAsync(ct);

        var next = items.Count > PageSize ? EncodeCursor(items[PageSize - 1]) : null;
        return TypedResults.Ok(new AuditPage(items.Take(PageSize).ToList(), next));
    }

    private static string EncodeCursor(AuditEntryResponse last) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{last.OccurredAt.UtcTicks}|{last.Id}"));

    private static bool TryDecodeCursor(string cursor, out DateTimeOffset at, out Guid id)
    {
        at = default;
        id = default;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || !Guid.TryParse(parts[1], out id))
            {
                return false;
            }

            at = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
