using Subscribely.Infrastructure.Auditing;

namespace Subscribely.Modules.Audit.Contracts;

/// <summary>Trilha do data plane (banco do tenant atual), implementada pelo módulo Projects.</summary>
public interface IProductAuditSource
{
    IQueryable<AuditEntry> Entries { get; }
}
