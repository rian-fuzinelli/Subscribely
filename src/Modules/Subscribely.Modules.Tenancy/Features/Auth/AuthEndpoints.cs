using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Auditing;
using Subscribely.Infrastructure.Caching;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Tenancy.Domain;
using Subscribely.Modules.Tenancy.Persistence;
using Subscribely.Modules.Tenancy.Security;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Tenancy.Features.Auth;

internal sealed class LoginRequest
{
    [Required]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string Password { get; init; } = string.Empty;
}

internal sealed class RefreshRequest
{
    [Required]
    [StringLength(200)]
    public string RefreshToken { get; init; } = string.Empty;
}

internal sealed class PasswordResetRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;
}

internal sealed class PasswordResetConfirmRequest
{
    [Required]
    [StringLength(200)]
    public string Token { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string NewPassword { get; init; } = string.Empty;
}

internal static class AuthEndpoints
{
    private const int LoginAttemptsPerMinute = 5;
    private const int ResetRequestsPerHour = 3;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/v1/auth").WithTags("Auth").AllowAnonymous();
        auth.MapPost("/login", LoginAsync).WithRequestValidation<LoginRequest>().Produces<TokenPair>();
        auth.MapPost("/refresh", RefreshAsync).WithRequestValidation<RefreshRequest>().Produces<TokenPair>();
        auth.MapPost("/logout", LogoutAsync).WithRequestValidation<RefreshRequest>();
        auth.MapPost("/password-reset", RequestPasswordResetAsync).WithRequestValidation<PasswordResetRequest>();
        auth.MapPost("/password-reset/confirm", ConfirmPasswordResetAsync).WithRequestValidation<PasswordResetConfirmRequest>();
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext http,
        TenancyDbContext db,
        Passwords passwords,
        TokenService tokens,
        RedisRateLimiter limiter,
        TenantContext tenantContext,
        IAuditLog audit,
        CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!await limiter.TryAcquireAsync($"login:{ip}:{Secrets.Hash(email)}", LoginAttemptsPerMinute, TimeSpan.FromMinutes(1)))
        {
            return Error.TooManyRequests("too-many-login-attempts", "Too many login attempts. Try again later.").ToProblem();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !user.IsActive || !passwords.Verify(user, request.Password))
        {
            await audit.WriteAsync("auth.login_failed", "User", user?.Id.ToString(), new { emailHash = Secrets.Hash(email) }, user?.TenantId, ct);
            return Error.Unauthorized("invalid-credentials", "Invalid e-mail or password.").ToProblem();
        }

        Tenant? tenant = null;
        if (user.TenantId is { } tenantId)
        {
            tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            if (tenant is not { IsActive: true })
            {
                return Error.Forbidden("tenant-unavailable", "The organization is not available.").ToProblem();
            }
        }

        tenantContext.Set(tenant?.Id, tenant?.Slug, ActorType.User, user.Id.ToString(), user.Role);
        var pair = await tokens.IssueAsync(user, tenant, ct);
        await audit.WriteAsync("auth.login_succeeded", "User", user.Id.ToString(), cancellationToken: ct);
        return TypedResults.Ok(pair);
    }

    private static async Task<IResult> RefreshAsync(RefreshRequest request, TokenService tokens, IAuditLog audit, CancellationToken ct)
    {
        var (outcome, pair, _) = await tokens.RefreshAsync(request.RefreshToken, ct);
        switch (outcome)
        {
            case RefreshOutcome.Rotated:
                return TypedResults.Ok(pair);
            case RefreshOutcome.ReuseDetected:
                await audit.WriteAsync("auth.refresh_reuse_detected", cancellationToken: ct);
                return Error.Unauthorized("invalid-refresh-token", "Invalid refresh token.").ToProblem();
            default:
                return Error.Unauthorized("invalid-refresh-token", "Invalid refresh token.").ToProblem();
        }
    }

    private static async Task<IResult> LogoutAsync(RefreshRequest request, TenancyDbContext db, TokenService tokens, CancellationToken ct)
    {
        var hash = Secrets.Hash(request.RefreshToken);
        var familyId = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => (Guid?)t.FamilyId).FirstOrDefaultAsync(ct);
        if (familyId is not null)
        {
            await tokens.RevokeFamilyAsync(familyId.Value, ct);
        }

        return TypedResults.NoContent();
    }

    /// <summary>Sempre 202: não revela se o e-mail existe nem se o limite foi atingido.</summary>
    private static async Task<IResult> RequestPasswordResetAsync(
        PasswordResetRequest request, TenancyDbContext db, RedisRateLimiter limiter, TimeProvider time, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);
        if (await limiter.TryAcquireAsync($"password-reset:{Secrets.Hash(email)}", ResetRequestsPerHour, TimeSpan.FromHours(1)))
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            if (user is { IsActive: true })
            {
                db.PasswordResetTokens.Add(PasswordResetToken.Issue(user, time.GetUtcNow()));
                await db.SaveChangesAsync(ct);
            }
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> ConfirmPasswordResetAsync(
        PasswordResetConfirmRequest request,
        TenancyDbContext db,
        Passwords passwords,
        TokenService tokens,
        TimeProvider time,
        IAuditLog audit,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var hash = Secrets.Hash(request.Token);
        var reset = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        var user = reset is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == reset.UserId, ct);
        if (reset is null || !reset.IsUsable(now) || user is not { IsActive: true })
        {
            return Error.Validation("invalid-reset-token", "The reset token is invalid or expired.").ToProblem();
        }

        user.ChangePassword(passwords.Hash(user, request.NewPassword));
        reset.MarkUsed(now);
        audit.Record("auth.password_reset", "User", user.Id.ToString(), tenantId: user.TenantId);
        await db.SaveChangesAsync(ct);
        await tokens.RevokeAllForUserAsync(user.Id, ct);
        return TypedResults.NoContent();
    }
}
