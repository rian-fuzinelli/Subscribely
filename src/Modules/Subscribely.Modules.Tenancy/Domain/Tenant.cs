using Subscribely.Infrastructure.Auditing;
using Subscribely.Modules.Tenancy.Contracts;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Tenancy.Domain;

internal enum TenantStatus
{
    Provisioning,
    Active,
    ProvisioningFailed,
}

/// <summary>Evento interno: dispara o job de provisionamento do banco do tenant.</summary>
internal sealed record TenantSignedUp(Guid TenantId) : IIntegrationEvent;

[Auditable]
internal sealed class Tenant : Entity
{
    public const int MaxProvisioningAttempts = 3;

    private Tenant()
    {
    }

    public string Slug { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public TenantStatus Status { get; private set; }

    public string DatabaseName { get; private set; } = string.Empty;

    public string SignupPlanCode { get; private set; } = string.Empty;

    public string SignupInterval { get; private set; } = string.Empty;

    public int ProvisioningAttempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsActive => Status == TenantStatus.Active;

    public static Tenant SignUp(string slug, string name, string databaseName, string planCode, string interval, DateTimeOffset now)
    {
        var tenant = new Tenant
        {
            Slug = slug,
            Name = name.Trim(),
            DatabaseName = databaseName,
            Status = TenantStatus.Provisioning,
            SignupPlanCode = planCode,
            SignupInterval = interval,
            CreatedAt = now,
        };
        tenant.Raise(new TenantSignedUp(tenant.Id));
        return tenant;
    }

    /// <summary>Idempotente: só publica <see cref="TenantProvisioned"/> na primeira ativação.</summary>
    public void MarkProvisioned()
    {
        if (Status == TenantStatus.Active)
        {
            return;
        }

        Status = TenantStatus.Active;
        Raise(new TenantProvisioned(Id, SignupPlanCode, SignupInterval));
    }

    /// <returns><c>true</c> quando as tentativas se esgotaram e o tenant foi marcado como falho.</returns>
    public bool RegisterProvisioningFailure()
    {
        ProvisioningAttempts++;
        if (ProvisioningAttempts < MaxProvisioningAttempts)
        {
            return false;
        }

        Status = TenantStatus.ProvisioningFailed;
        return true;
    }
}
