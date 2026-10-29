using System.Net;
using Microsoft.AspNetCore.Http;
using Subscribely.Infrastructure.Tenancy;

namespace Subscribely.Infrastructure.Auditing;

public sealed record AuditActor(ActorType Type, string? Id, string? CorrelationId, IPAddress? Ip);

/// <summary>Resolve quem está agindo (usuário, API key, job ou webhook) a partir do contexto atual.</summary>
public sealed class AuditActorAccessor(ITenantContext tenantContext, IHttpContextAccessor httpContextAccessor)
{
    public AuditActor Current
    {
        get
        {
            var http = httpContextAccessor.HttpContext;
            return new AuditActor(
                tenantContext.ActorType,
                tenantContext.ActorId ?? (tenantContext.ActorType == ActorType.Anonymous ? "anonymous" : null),
                http?.TraceIdentifier,
                http?.Connection.RemoteIpAddress);
        }
    }
}

/// <summary>Ações explícitas (login, webhook, void...). Para mudanças de entidade use <see cref="AuditableAttribute"/>.</summary>
public interface IAuditLog
{
    /// <summary>Enfileira a entrada; ela é gravada na mesma transação do próximo SaveChanges de qualquer módulo.</summary>
    void Record(string action, string? entityType = null, string? entityId = null, object? data = null, Guid? tenantId = null);

    /// <summary>Grava imediatamente (para ações sem mudança de estado, ex.: login falho).</summary>
    Task WriteAsync(string action, string? entityType = null, string? entityId = null, object? data = null, Guid? tenantId = null, CancellationToken cancellationToken = default);
}

/// <summary>Buffer por escopo das entradas explícitas pendentes.</summary>
internal sealed class AuditBuffer
{
    private readonly List<AuditEntry> _pending = [];

    public void Add(AuditEntry entry) => _pending.Add(entry);

    public IReadOnlyList<AuditEntry> Drain()
    {
        var entries = _pending.ToList();
        _pending.Clear();
        return entries;
    }
}
