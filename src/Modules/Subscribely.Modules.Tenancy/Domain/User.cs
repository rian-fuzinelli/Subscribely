using Subscribely.Infrastructure.Auditing;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Tenancy.Contracts;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Tenancy.Domain;

internal enum UserStatus
{
    Invited,
    Active,
    Disabled,
}

[Auditable]
internal sealed class User : Entity
{
    private User()
    {
    }

    public Guid? TenantId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    [NotAudited]
    public string PasswordHash { get; private set; } = string.Empty;

    public string Role { get; private set; } = string.Empty;

    public UserStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsActive => Status == UserStatus.Active;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static User CreateActive(Guid? tenantId, string email, string role, Func<User, string> hashPassword, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(hashPassword);

        var user = new User
        {
            TenantId = tenantId,
            Email = NormalizeEmail(email),
            Role = role,
            Status = UserStatus.Active,
            CreatedAt = now,
        };
        user.PasswordHash = hashPassword(user);
        if (tenantId is not null)
        {
            user.Raise(new UserActivated(tenantId.Value, user.Id));
        }

        return user;
    }

    public void ChangePassword(string passwordHash) => PasswordHash = passwordHash;

    public void Disable() => Status = UserStatus.Disabled;

    public void Enable() => Status = UserStatus.Active;

    public bool IsTenantAdmin => Role == Roles.TenantAdmin;
}
