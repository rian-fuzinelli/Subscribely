namespace Subscribely.Infrastructure.Tenancy;

public enum ActorType
{
    Anonymous,
    User,
    ApiKey,
    System,
    Webhook,
}

/// <summary>Única fonte do tenant e do ator da requisição/job atual. Nunca leia o "tid" do request diretamente.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    string? TenantSlug { get; }

    ActorType ActorType { get; }

    string? ActorId { get; }

    string? Role { get; }

    bool IsPlatformAdmin { get; }

    Guid RequiredTenantId { get; }
}

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public string? TenantSlug { get; private set; }

    public ActorType ActorType { get; private set; } = ActorType.Anonymous;

    public string? ActorId { get; private set; }

    public string? Role { get; private set; }

    public bool IsPlatformAdmin => Role == Roles.PlatformAdmin;

    public Guid RequiredTenantId =>
        TenantId ?? throw new InvalidOperationException("No tenant resolved for the current context.");

    public void Set(Guid? tenantId, string? tenantSlug, ActorType actorType, string? actorId, string? role = null)
    {
        TenantId = tenantId;
        TenantSlug = tenantSlug;
        ActorType = actorType;
        ActorId = actorId;
        Role = role;
    }

    /// <summary>Usado por jobs e consumidores para agir em nome do sistema dentro de um tenant.</summary>
    public void SetSystem(string jobName, Guid? tenantId = null, string? tenantSlug = null) =>
        Set(tenantId, tenantSlug, ActorType.System, jobName);
}

public static class Roles
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string Member = "Member";
}

public static class SubscribelyClaims
{
    public const string TenantId = "tid";
    public const string TenantSlug = "tslug";
    public const string Role = "role";
    public const string ApiKeyId = "kid";
}
