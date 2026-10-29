using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subscribely.Infrastructure.Tenancy;

namespace Subscribely.Infrastructure.Auditing;

/// <summary>Marca uma entidade cujas mudanças são auditadas automaticamente no SaveChanges.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditableAttribute : Attribute;

/// <summary>Propriedade que nunca entra no JSON de auditoria (hashes, tokens, segredos).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;

/// <summary>Entrada imutável da trilha de auditoria (append-only no banco).</summary>
public sealed class AuditEntry
{
    public const int MaxActionLength = 100;

    private AuditEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid? TenantId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public ActorType ActorType { get; private set; }

    public string? ActorId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string? EntityType { get; private set; }

    public string? EntityId { get; private set; }

    public string? Before { get; private set; }

    public string? After { get; private set; }

    public string? CorrelationId { get; private set; }

    public IPAddress? Ip { get; private set; }

    internal static AuditEntry Create(AuditActor actor, Guid? tenantId, string action, string? entityType, string? entityId, string? before, string? after, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        OccurredAt = now,
        ActorType = actor.Type,
        ActorId = actor.Id,
        Action = action,
        EntityType = entityType,
        EntityId = entityId,
        Before = before,
        After = after,
        CorrelationId = actor.CorrelationId,
        Ip = actor.Ip,
    };
}

/// <summary>Control plane: <c>audit.entries</c> no Catálogo. Data plane: <c>public.audit_entries</c> no banco do tenant.</summary>
public sealed class AuditEntryConfiguration(string schema, string table, bool ownsTable) : IEntityTypeConfiguration<AuditEntry>
{
    public const string ControlSchema = "audit";

    public static AuditEntryConfiguration ControlPlane(bool ownsTable) => new(ControlSchema, "entries", ownsTable);

    public static AuditEntryConfiguration DataPlane() => new("public", "audit_entries", ownsTable: true);

    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(table, schema, t =>
        {
            if (!ownsTable)
            {
                t.ExcludeFromMigrations();
            }
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ActorType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ActorId).HasMaxLength(100);
        builder.Property(e => e.Action).HasMaxLength(AuditEntry.MaxActionLength);
        builder.Property(e => e.EntityType).HasMaxLength(100);
        builder.Property(e => e.EntityId).HasMaxLength(100);
        builder.Property(e => e.Before).HasColumnType("jsonb");
        builder.Property(e => e.After).HasColumnType("jsonb");
        builder.Property(e => e.CorrelationId).HasMaxLength(64);
        builder.HasIndex(e => new { e.TenantId, e.OccurredAt, e.Id });
        builder.HasIndex(e => new { e.Action, e.OccurredAt });
    }
}
