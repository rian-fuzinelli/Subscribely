using System.Security.Cryptography;
using System.Text;
using Subscribely.Infrastructure.Auditing;
using Subscribely.Modules.Tenancy.Contracts;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Tenancy.Domain;

/// <summary>Segredos aleatórios e hashes SHA-256 (o segredo em claro nunca é persistido).</summary>
internal static class Secrets
{
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string NewUrlToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string NewBase62(int length) => RandomNumberGenerator.GetString(Base62, length);

    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static bool HashEquals(string secret, string expectedHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret)), Encoding.ASCII.GetBytes(expectedHash));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    [NotAudited]
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedById { get; private set; }

    /// <summary>Já foi trocado ou revogado: reapresentá-lo indica roubo (reuso).</summary>
    public bool IsSpent => RevokedAt is not null || ReplacedById is not null;

    public static (RefreshToken Token, string Secret) Issue(Guid userId, Guid familyId, DateTimeOffset now, TimeSpan lifetime)
    {
        var secret = Secrets.NewUrlToken();
        var token = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Secrets.Hash(secret),
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };
        return (token, secret);
    }

    public void ReplaceWith(RefreshToken next, DateTimeOffset now)
    {
        ReplacedById = next.Id;
        RevokedAt = now;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

[Auditable]
internal sealed class Invitation : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    private Invitation()
    {
    }

    public Guid TenantId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string Role { get; private set; } = string.Empty;

    [NotAudited]
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsPending(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && ExpiresAt > now;

    public static Invitation Create(Guid tenantId, string email, string role, DateTimeOffset now)
    {
        var token = Secrets.NewUrlToken();
        var invitation = new Invitation
        {
            TenantId = tenantId,
            Email = User.NormalizeEmail(email),
            Role = role,
            TokenHash = Secrets.Hash(token),
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
        invitation.Raise(new UserInvited(tenantId, invitation.Id, invitation.Email, token, invitation.ExpiresAt));
        return invitation;
    }

    public void Accept(DateTimeOffset now) => AcceptedAt = now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

[Auditable]
internal sealed class ApiKey : Entity
{
    public const string Prefix = "sk_live_";

    private ApiKey()
    {
    }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string KeyPrefix { get; private set; } = string.Empty;

    [NotAudited]
    public string KeyHash { get; private set; } = string.Empty;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Formato: <c>sk_live_{prefixo8}_{segredo32}</c>; só o prefixo e o hash do segredo são guardados.</summary>
    public static (ApiKey Key, string PlainText) Create(Guid tenantId, string name, Guid createdBy, DateTimeOffset now)
    {
        var keyPrefix = Secrets.NewBase62(8);
        var secret = Secrets.NewBase62(32);
        var key = new ApiKey
        {
            TenantId = tenantId,
            Name = name.Trim(),
            KeyPrefix = keyPrefix,
            KeyHash = Secrets.Hash(secret),
            CreatedBy = createdBy,
            CreatedAt = now,
        };
        return (key, $"{Prefix}{keyPrefix}_{secret}");
    }

    public static bool TryParse(string? plainText, out string keyPrefix, out string secret)
    {
        keyPrefix = secret = string.Empty;
        if (plainText is null || !plainText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = plainText[Prefix.Length..].Split('_');
        if (parts.Length != 2 || parts[0].Length != 8 || parts[1].Length != 32)
        {
            return false;
        }

        (keyPrefix, secret) = (parts[0], parts[1]);
        return true;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

internal sealed class PasswordResetToken : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private PasswordResetToken()
    {
    }

    public Guid UserId { get; private set; }

    [NotAudited]
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;

    public static PasswordResetToken Issue(User user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        var token = Secrets.NewUrlToken();
        var reset = new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = Secrets.Hash(token),
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
        reset.Raise(new PasswordResetRequested(reset.Id, user.Id, user.Email, token, reset.ExpiresAt));
        return reset;
    }

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;
}
