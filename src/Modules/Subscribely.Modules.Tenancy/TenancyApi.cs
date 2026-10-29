using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Tenancy.Contracts;
using Subscribely.Modules.Tenancy.Domain;
using Subscribely.Modules.Tenancy.Persistence;

namespace Subscribely.Modules.Tenancy;

/// <summary>Consultas explícitas por tenant (sempre filtram pelo <c>tenantId</c> recebido).</summary>
internal sealed class TenancyApi(TenancyDbContext db, TimeProvider time) : ITenancyApi
{
    public async Task<TenantInfo?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantInfo(t.Id, t.Slug, t.Name, t.Status.ToString()))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> CountOccupiedSeatsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var activeUsers = await db.Users.IgnoreQueryFilters()
            .CountAsync(u => u.TenantId == tenantId && u.Status == UserStatus.Active, cancellationToken);
        var pendingInvitations = await db.Invitations.IgnoreQueryFilters()
            .CountAsync(i => i.TenantId == tenantId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now, cancellationToken);
        return activeUsers + pendingInvitations;
    }

    public async Task<IReadOnlyList<AdminContact>> ListActiveAdminsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.Role == Roles.TenantAdmin && u.Status == UserStatus.Active)
            .OrderBy(u => u.Email)
            .Select(u => new AdminContact(u.Id, u.Email))
            .ToListAsync(cancellationToken);
}
