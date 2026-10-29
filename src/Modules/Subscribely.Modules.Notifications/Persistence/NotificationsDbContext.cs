using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subscribely.Infrastructure.Persistence;

namespace Subscribely.Modules.Notifications.Persistence;

internal enum EmailStatus
{
    Pending,
    Sent,
    Failed,

    /// <summary><c>Smtp:Enabled = false</c>: registrado, nada enviado.</summary>
    Skipped,
}

/// <summary>Um e-mail por destinatário e evento; <see cref="DedupKey"/> UNIQUE impede reenvio em reprocessamento.</summary>
internal sealed class EmailLog
{
    public const int MaxAttempts = 5;

    private EmailLog()
    {
    }

    public Guid Id { get; private set; }

    public Guid? TenantId { get; private set; }

    public string Template { get; private set; } = string.Empty;

    public string ToAddress { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string DedupKey { get; private set; } = string.Empty;

    public EmailStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Já enviado, desligado ou sem mais tentativas: não enviar de novo.</summary>
    public bool IsSettled => Status is EmailStatus.Sent or EmailStatus.Skipped || Attempts >= MaxAttempts;

    public static EmailLog Reserve(Guid? tenantId, string template, string toAddress, string subject, string dedupKey, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Template = template,
        ToAddress = toAddress,
        Subject = subject,
        DedupKey = dedupKey,
        Status = EmailStatus.Pending,
        CreatedAt = now,
    };

    public void MarkSent(DateTimeOffset now)
    {
        Attempts++;
        Status = EmailStatus.Sent;
        SentAt = now;
        LastError = null;
    }

    public void MarkSkipped() => Status = EmailStatus.Skipped;

    public void MarkFailed(string error)
    {
        Attempts++;
        Status = EmailStatus.Failed;
        LastError = error.Length > 1_000 ? error[..1_000] : error;
    }
}

internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "notifications";

    public override string Schema => SchemaName;

    public DbSet<EmailLog> EmailLog => Set<EmailLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new EmailLogConfiguration());
    }
}

internal sealed class EmailLogConfiguration : IEntityTypeConfiguration<EmailLog>
{
    public void Configure(EntityTypeBuilder<EmailLog> builder)
    {
        builder.ToTable("email_log");
        builder.HasKey(e => e.Id);
        builder.Ignore(e => e.IsSettled);
        builder.Property(e => e.Template).HasMaxLength(40);
        builder.Property(e => e.ToAddress).HasMaxLength(254);
        builder.Property(e => e.Subject).HasMaxLength(200);
        builder.Property(e => e.DedupKey).HasMaxLength(200);
        builder.HasIndex(e => e.DedupKey).IsUnique();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.LastError).HasMaxLength(1_000);
        builder.HasIndex(e => new { e.TenantId, e.CreatedAt });
    }
}
