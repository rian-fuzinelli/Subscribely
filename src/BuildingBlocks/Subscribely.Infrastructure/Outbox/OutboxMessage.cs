using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subscribely.SharedKernel;

namespace Subscribely.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public const int MaxAttempts = 5;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Nome do tipo qualificado pelo assembly (<c>Namespace.Tipo, Assembly</c>).</summary>
    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage From(IIntegrationEvent @event, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var type = @event.GetType();

        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = $"{type.FullName}, {type.Assembly.GetName().Name}",
            Payload = JsonSerializer.Serialize(@event, type, JsonOptions),
            OccurredAt = occurredAt,
        };
    }

    internal IIntegrationEvent Deserialize()
    {
        var type = System.Type.GetType(Type, throwOnError: true)!;
        return (IIntegrationEvent)(JsonSerializer.Deserialize(Payload, type, JsonOptions)
            ?? throw new InvalidOperationException($"Outbox message {Id} has an empty payload."));
    }

    internal void MarkProcessed(DateTimeOffset at) => ProcessedAt = at;

    internal void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
    }
}

internal sealed class OutboxMessageConfiguration(bool ownsTable) : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("messages", OutboxDbContext.SchemaName, t =>
        {
            if (!ownsTable)
            {
                t.ExcludeFromMigrations();
            }
        });
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(500);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.HasIndex(m => m.OccurredAt).HasFilter("processed_at IS NULL");
    }
}
