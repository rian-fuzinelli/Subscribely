using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Notifications.Persistence;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Notifications.Features;

internal sealed record EmailLogResponse(
    Guid Id,
    Guid? TenantId,
    string Template,
    string ToAddress,
    string Subject,
    string Status,
    int Attempts,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);

internal static class NotificationEndpoints
{
    private const int MaxRows = 200;

    public static IEndpointRouteBuilder MapNotificationAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/notifications", ListAsync).WithTags("Notifications (admin)").RequirePlatformAdmin();
        return endpoints;
    }

    private static async Task<IResult> ListAsync(Guid? tenantId, string? status, NotificationsDbContext db, CancellationToken ct)
    {
        var query = db.EmailLog.AsNoTracking();
        if (tenantId is { } id)
        {
            query = query.Where(e => e.TenantId == id);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<EmailStatus>(status, ignoreCase: true, out var parsed))
            {
                return Error.Validation("status-invalid", $"Unknown status '{status}'.").ToProblem();
            }

            query = query.Where(e => e.Status == parsed);
        }

        return TypedResults.Ok(await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(MaxRows)
            .Select(e => new EmailLogResponse(e.Id, e.TenantId, e.Template, e.ToAddress, e.Subject, e.Status.ToString(), e.Attempts, e.LastError, e.CreatedAt, e.SentAt))
            .ToListAsync(ct));
    }
}
