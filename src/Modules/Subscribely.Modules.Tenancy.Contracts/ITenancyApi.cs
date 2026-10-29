using Subscribely.SharedKernel;

namespace Subscribely.Modules.Tenancy.Contracts;

public sealed record TenantInfo(Guid Id, string Slug, string Name, string Status);

public sealed record AdminContact(Guid UserId, string Email);

/// <summary>API síncrona do módulo Tenancy para os demais módulos.</summary>
public interface ITenancyApi
{
    Task<TenantInfo?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Assentos ocupados: usuários ativos + convites pendentes não expirados.</summary>
    Task<int> CountOccupiedSeatsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminContact>> ListActiveAdminsAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Slugs dos tenants com banco provisionado (para aplicar migrations do data plane).</summary>
    Task<IReadOnlyList<string>> ListActiveTenantSlugsAsync(CancellationToken cancellationToken);
}

/// <summary>Banco do tenant criado e migrado; o Billing inicia o trial.</summary>
public sealed record TenantProvisioned(Guid TenantId, string PlanCode, string Interval) : IIntegrationEvent;

/// <summary>Convite criado. O token só trafega no outbox até o e-mail; nunca é logado.</summary>
public sealed record UserInvited(Guid TenantId, Guid InvitationId, string Email, string Token, DateTimeOffset ExpiresAt) : IIntegrationEvent;

public sealed record UserActivated(Guid TenantId, Guid UserId) : IIntegrationEvent;

/// <summary>Pedido de redefinição de senha. O token só trafega no outbox até o e-mail; nunca é logado.</summary>
public sealed record PasswordResetRequested(Guid TokenId, Guid UserId, string Email, string Token, DateTimeOffset ExpiresAt) : IIntegrationEvent;
