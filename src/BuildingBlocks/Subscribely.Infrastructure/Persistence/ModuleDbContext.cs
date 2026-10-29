using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Auditing;
using Subscribely.Infrastructure.Outbox;
using Subscribely.SharedKernel;

namespace Subscribely.Infrastructure.Persistence;

/// <summary>
/// Base de todo DbContext de módulo no banco Catálogo: um schema por módulo e outbox transacional
/// (eventos das entidades e os publicados via <see cref="Publish"/> viram linhas em <c>outbox.messages</c>
/// no mesmo <c>SaveChanges</c>).
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    private readonly List<IIntegrationEvent> _pendingEvents = [];

    public abstract string Schema { get; }

    internal IReadOnlyList<IIntegrationEvent> PendingEvents => _pendingEvents;

    /// <summary>Publica um evento que não nasce de uma entidade; é gravado no próximo <c>SaveChanges</c>.</summary>
    public void Publish(IIntegrationEvent @event) => _pendingEvents.Add(@event);

    internal void ClearPendingEvents() => _pendingEvents.Clear();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Conventions.Add(_ => new ClientGeneratedKeysConvention());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(ownsTable: Schema == OutboxDbContext.SchemaName));
        modelBuilder.ApplyConfiguration(AuditEntryConfiguration.ControlPlane(ownsTable: Schema == AuditEntryConfiguration.ControlSchema));
    }
}
