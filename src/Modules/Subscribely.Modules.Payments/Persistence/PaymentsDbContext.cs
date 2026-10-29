using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subscribely.Infrastructure.Persistence;
using Subscribely.Modules.Payments.Domain;

namespace Subscribely.Modules.Payments.Persistence;

internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "payments";

    public override string Schema => SchemaName;

    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();

    public DbSet<Charge> Charges => Set<Charge>();

    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    public DbSet<Dunning> Dunnings => Set<Dunning>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
    }
}

internal sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("payment_methods");
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => m.TenantId).IsUnique();
        builder.Property(m => m.GatewayToken).HasMaxLength(100);
        builder.Property(m => m.Brand).HasMaxLength(20);
        builder.Property(m => m.Last4).HasMaxLength(4);
    }
}

internal sealed class ChargeConfiguration : IEntityTypeConfiguration<Charge>
{
    public void Configure(EntityTypeBuilder<Charge> builder)
    {
        builder.ToTable("charges", t => t.HasCheckConstraint("ck_charges_amount", "amount_cents > 0"));
        builder.HasKey(c => c.Id);
        builder.Ignore(c => c.IsTerminal);
        builder.Property(c => c.IdempotencyKey).HasMaxLength(60);
        builder.HasIndex(c => c.IdempotencyKey).IsUnique();
        builder.HasIndex(c => new { c.InvoiceId, c.Attempt }).IsUnique();
        builder.Property(c => c.GatewayChargeId).HasMaxLength(60);
        builder.HasIndex(c => c.GatewayChargeId);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.FailureCode).HasMaxLength(60);

        // No máximo uma charge pendente por fatura (o "pagar agora" concorrente recebe 409).
        builder.HasIndex(c => c.InvoiceId).IsUnique().HasFilter("status = 'Pending'").HasDatabaseName("ix_charges_one_pending_per_invoice");
    }
}

internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable("payment_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Provider).HasMaxLength(20);
        builder.Property(e => e.ProviderEventId).HasMaxLength(100);
        builder.HasIndex(e => new { e.Provider, e.ProviderEventId }).IsUnique();
        builder.Property(e => e.Type).HasMaxLength(50);
        builder.Property(e => e.Payload).HasColumnType("jsonb");
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Note).HasMaxLength(200);
    }
}

internal sealed class DunningConfiguration : IEntityTypeConfiguration<Dunning>
{
    public void Configure(EntityTypeBuilder<Dunning> builder)
    {
        builder.ToTable("dunning");
        builder.HasKey(d => d.InvoiceId);
        builder.Ignore(d => d.Id);
        builder.Ignore(d => d.Events);
        builder.Ignore(d => d.IsOpen);
        builder.HasIndex(d => d.NextAttemptAt).HasFilter("exhausted_at IS NULL AND resolved_at IS NULL");
    }
}
