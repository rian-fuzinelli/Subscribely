using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Subscribely.Infrastructure.Tenancy;

namespace Subscribely.Infrastructure.Auditing;

/// <summary>
/// Grava as entradas de auditoria na mesma transação do SaveChanges: mudanças de entidades
/// <see cref="AuditableAttribute"/> e as ações explícitas pendentes no <see cref="AuditBuffer"/>.
/// </summary>
internal sealed class AuditInterceptor(
    AuditActorAccessor actorAccessor,
    ITenantContext tenantContext,
    AuditBuffer buffer,
    TimeProvider timeProvider) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        WriteAudit(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        WriteAudit(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void WriteAudit(DbContext? context)
    {
        if (context is null || context.Model.FindEntityType(typeof(AuditEntry)) is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var actor = actorAccessor.Current;
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Metadata.ClrType.GetCustomAttribute<AuditableAttribute>() is not null)
            .Select(e => FromChange(e, actor, now))
            .Where(e => e is not null)
            .Cast<AuditEntry>()
            .Concat(buffer.Drain())
            .ToList();

        if (entries.Count > 0)
        {
            context.Set<AuditEntry>().AddRange(entries);
        }
    }

    private AuditEntry? FromChange(EntityEntry entry, AuditActor actor, DateTimeOffset now)
    {
        var audited = entry.Properties
            .Where(p => p.Metadata.PropertyInfo?.GetCustomAttribute<NotAuditedAttribute>() is null)
            .ToList();

        var changed = entry.State == EntityState.Modified ? audited.Where(p => p.IsModified).ToList() : audited;
        if (changed.Count == 0)
        {
            return null;
        }

        string? before = entry.State == EntityState.Added
            ? null
            : Serialize(changed.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue));
        string? after = entry.State == EntityState.Deleted
            ? null
            : Serialize(changed.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));

        var verb = entry.State switch
        {
            EntityState.Added => "created",
            EntityState.Deleted => "deleted",
            _ => "updated",
        };
        var entityName = entry.Metadata.ClrType.Name;
        var entityId = string.Join(',', entry.Metadata.FindPrimaryKey()!.Properties.Select(p => entry.Property(p.Name).CurrentValue));
        var tenantId = entry.Metadata.FindProperty("TenantId") is { } tenantProperty
            ? entry.Property(tenantProperty.Name).CurrentValue as Guid?
            : tenantContext.TenantId;

        return AuditEntry.Create(actor, tenantId, $"{ToSnakeCase(entityName)}.{verb}", entityName, entityId, before, after, now);
    }

    private static string Serialize(Dictionary<string, object?> values) => JsonSerializer.Serialize(values, JsonOptions);

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}

/// <summary>Implementação do <see cref="IAuditLog"/> que usa o buffer (Record) ou um contexto dedicado (WriteAsync).</summary>
internal sealed class AuditLog(AuditBuffer buffer, AuditActorAccessor actorAccessor, ITenantContext tenantContext, TimeProvider timeProvider, IAuditWriter writer) : IAuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(string action, string? entityType = null, string? entityId = null, object? data = null, Guid? tenantId = null) =>
        buffer.Add(Create(action, entityType, entityId, data, tenantId));

    public Task WriteAsync(string action, string? entityType = null, string? entityId = null, object? data = null, Guid? tenantId = null, CancellationToken cancellationToken = default) =>
        writer.WriteAsync(Create(action, entityType, entityId, data, tenantId), cancellationToken);

    private AuditEntry Create(string action, string? entityType, string? entityId, object? data, Guid? tenantId) =>
        AuditEntry.Create(
            actorAccessor.Current,
            tenantId ?? tenantContext.TenantId,
            action,
            entityType,
            entityId,
            before: null,
            after: data is null ? null : JsonSerializer.Serialize(data, JsonOptions),
            timeProvider.GetUtcNow());
}

/// <summary>Grava uma entrada fora de qualquer transação de negócio (implementado pelo módulo Audit).</summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);
}
