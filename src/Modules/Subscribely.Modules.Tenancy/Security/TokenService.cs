using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Tenancy.Domain;
using Subscribely.Modules.Tenancy.Persistence;

namespace Subscribely.Modules.Tenancy.Security;

internal sealed record TokenPair(string AccessToken, int ExpiresIn, string RefreshToken);

internal enum RefreshOutcome
{
    Rotated,
    Invalid,
    ReuseDetected,
}

/// <summary>Emite access tokens (JWT, 15 min) e refresh tokens rotativos com detecção de reuso por família.</summary>
internal sealed class TokenService(IOptions<JwtOptions> options, TenancyDbContext db, TimeProvider time)
{
    private readonly JwtOptions _jwt = options.Value;

    public async Task<TokenPair> IssueAsync(User user, Tenant? tenant, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = time.GetUtcNow();
        var (refresh, secret) = RefreshToken.Issue(user.Id, Guid.CreateVersion7(), now, _jwt.RefreshTokenLifetime);
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(ct);
        return new TokenPair(CreateAccessToken(user, tenant, now), (int)_jwt.AccessTokenLifetime.TotalSeconds, secret);
    }

    public async Task<(RefreshOutcome Outcome, TokenPair? Tokens, User? User)> RefreshAsync(string presented, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var hash = Secrets.Hash(presented);
        var current = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (current is null)
        {
            return (RefreshOutcome.Invalid, null, null);
        }

        if (current.IsSpent)
        {
            await RevokeFamilyAsync(current.FamilyId, ct);
            return (RefreshOutcome.ReuseDetected, null, null);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == current.UserId, ct);
        var tenant = user?.TenantId is { } tenantId ? await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct) : null;
        if (current.ExpiresAt <= now || user is not { IsActive: true } || (user.TenantId is not null && tenant is not { IsActive: true }))
        {
            return (RefreshOutcome.Invalid, null, null);
        }

        var (next, secret) = RefreshToken.Issue(user.Id, current.FamilyId, now, _jwt.RefreshTokenLifetime);
        current.ReplaceWith(next, now);
        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync(ct);
        return (RefreshOutcome.Rotated, new TokenPair(CreateAccessToken(user, tenant, now), (int)_jwt.AccessTokenLifetime.TotalSeconds, secret), user);
    }

    public async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private string CreateAccessToken(User user, Tenant? tenant, DateTimeOffset now)
    {
        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new(SubscribelyClaims.Role, user.Role),
            new("jti", Guid.NewGuid().ToString("N")),
        };
        if (tenant is not null)
        {
            claims.Add(new Claim(SubscribelyClaims.TenantId, tenant.Id.ToString()));
            claims.Add(new Claim(SubscribelyClaims.TenantSlug, tenant.Slug));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = (now + _jwt.AccessTokenLifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(_jwt.GetSigningKey(), SecurityAlgorithms.HmacSha256),
        });
    }
}

/// <summary>PBKDF2 do ASP.NET Identity.</summary>
internal sealed class Passwords
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
}
