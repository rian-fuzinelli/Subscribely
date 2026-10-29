using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Persistence;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Tenancy.Contracts;
using Subscribely.Modules.Tenancy.Domain;
using Subscribely.Modules.Tenancy.Persistence;
using Subscribely.Modules.Tenancy.Security;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Tenancy.Features.Users;

internal sealed class InviteUserRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^(TenantAdmin|Member)$", ErrorMessage = "Role must be TenantAdmin or Member.")]
    public string Role { get; init; } = Roles.Member;
}

internal sealed class AcceptInvitationRequest
{
    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;
}

internal sealed record UserResponse(Guid Id, string Email, string Role, string Status);

internal sealed record InvitationResponse(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt);

/// <summary>Assentos ocupados = ativos + convites pendentes; limite vem da assinatura (Billing).</summary>
internal sealed class SeatPolicy(ITenancyApi tenancy, IBillingApi billing)
{
    /// <summary>Sem assinatura ainda: só o admin do signup.</summary>
    public const int SeatsWithoutSubscription = 1;

    public async Task<Error?> EnsureSeatAvailableAsync(Guid tenantId, CancellationToken ct)
    {
        var maxSeats = (await billing.GetEntitlementsAsync(tenantId, ct))?.MaxSeats ?? SeatsWithoutSubscription;
        var occupied = await tenancy.CountOccupiedSeatsAsync(tenantId, ct);
        return occupied < maxSeats
            ? null
            : Error.Conflict("seat-limit-reached", "The plan seat limit has been reached.") with
            {
                Details = new Dictionary<string, object?> { ["occupiedSeats"] = occupied, ["maxSeats"] = maxSeats },
            };
    }
}

internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup("/v1/users").WithTags("Users");
        users.MapGet("/", ListUsersAsync).RequireTenantMember();
        users.MapPost("/{id:guid}/disable", DisableAsync).RequireTenantAdmin();
        users.MapPost("/{id:guid}/enable", EnableAsync).RequireTenantAdmin();
        users.MapGet("/invitations", ListInvitationsAsync).RequireTenantAdmin();
        users.MapPost("/invitations", InviteAsync).RequireTenantAdmin().WithRequestValidation<InviteUserRequest>();
        users.MapDelete("/invitations/{id:guid}", RevokeInvitationAsync).RequireTenantAdmin();

        endpoints.MapPost("/v1/invitations/{token}/accept", AcceptAsync)
            .WithTags("Users").AllowAnonymous().WithRequestValidation<AcceptInvitationRequest>();
        return endpoints;
    }

    private static async Task<IResult> ListUsersAsync(TenancyDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Users.AsNoTracking().OrderBy(u => u.Email)
            .Select(u => new UserResponse(u.Id, u.Email, u.Role, u.Status.ToString()))
            .ToListAsync(ct));

    private static async Task<IResult> InviteAsync(
        InviteUserRequest request, ITenantContext tenant, TenancyDbContext db, SeatPolicy seats, TimeProvider time, CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        var email = User.NormalizeEmail(request.Email);
        var now = time.GetUtcNow();

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
        {
            return Error.Conflict("email-unavailable", "This e-mail cannot be invited.").ToProblem();
        }

        if (await db.Invitations.AnyAsync(i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now, ct))
        {
            return Error.Conflict("invitation-exists", "There is already a pending invitation for this e-mail.").ToProblem();
        }

        if (await seats.EnsureSeatAvailableAsync(tenantId, ct) is { } seatError)
        {
            return seatError.ToProblem();
        }

        var invitation = Invitation.Create(tenantId, email, request.Role, now);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/v1/users/invitations/{invitation.Id}", ToResponse(invitation));
    }

    private static async Task<IResult> ListInvitationsAsync(TenancyDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return TypedResults.Ok(await db.Invitations.AsNoTracking()
            .Where(i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new InvitationResponse(i.Id, i.Email, i.Role, i.ExpiresAt))
            .ToListAsync(ct));
    }

    private static async Task<IResult> RevokeInvitationAsync(Guid id, TenancyDbContext db, TimeProvider time, CancellationToken ct)
    {
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invitation is null)
        {
            return Error.NotFound().ToProblem();
        }

        invitation.Revoke(time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> AcceptAsync(
        string token, AcceptInvitationRequest request, TenancyDbContext db, Passwords passwords, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var hash = Secrets.Hash(token);
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invitation is null || !invitation.IsPending(now))
        {
            return Error.Gone("invitation-expired", "The invitation is invalid or has expired.").ToProblem();
        }

        var user = User.CreateActive(invitation.TenantId, invitation.Email, invitation.Role, u => passwords.Hash(u, request.Password), now);
        invitation.Accept(now);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_users_email"))
        {
            return Error.Conflict("email-unavailable", "This e-mail cannot be used.").ToProblem();
        }

        return TypedResults.Created($"/v1/users/{user.Id}", new UserResponse(user.Id, user.Email, user.Role, user.Status.ToString()));
    }

    private static async Task<IResult> DisableAsync(Guid id, TenancyDbContext db, TokenService tokens, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return Error.NotFound().ToProblem();
        }

        if (user is { IsActive: true, IsTenantAdmin: true }
            && await db.Users.CountAsync(u => u.Role == Roles.TenantAdmin && u.Status == UserStatus.Active, ct) == 1)
        {
            return Error.Conflict("last-admin", "The last active TenantAdmin cannot be disabled.").ToProblem();
        }

        user.Disable();
        await db.SaveChangesAsync(ct);
        await tokens.RevokeAllForUserAsync(user.Id, ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> EnableAsync(Guid id, ITenantContext tenant, TenancyDbContext db, SeatPolicy seats, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return Error.NotFound().ToProblem();
        }

        if (user.IsActive)
        {
            return TypedResults.NoContent();
        }

        if (await seats.EnsureSeatAvailableAsync(tenant.RequiredTenantId, ct) is { } seatError)
        {
            return seatError.ToProblem();
        }

        user.Enable();
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static InvitationResponse ToResponse(Invitation i) => new(i.Id, i.Email, i.Role, i.ExpiresAt);
}
