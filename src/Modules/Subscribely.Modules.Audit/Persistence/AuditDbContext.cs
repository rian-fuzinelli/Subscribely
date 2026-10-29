using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Auditing;
using Subscribely.Infrastructure.Persistence;

namespace Subscribely.Modules.Audit.Persistence;

/// <summary>Dono das migrations de <c>audit.entries</c> (control plane).</summary>
internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : ModuleDbContext(options)
{
    public override string Schema => AuditEntryConfiguration.ControlSchema;

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();
}

/// <summary>Grava uma entrada imediatamente, fora de transações de negócio (ex.: login falho).</summary>
internal sealed class AuditWriter(AuditDbContext db) : IAuditWriter
{
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        db.Entries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }
}
