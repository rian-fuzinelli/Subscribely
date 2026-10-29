using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subscribely.Modules.Billing.Domain.Invoices;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Domain.Subscriptions;

namespace Subscribely.Modules.Billing.Persistence.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public const string NumberSequence = "invoice_number_seq";

    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", t =>
        {
            t.HasCheckConstraint("ck_invoices_total", "total_cents >= 0");
            t.HasCheckConstraint("ck_invoices_period", "period_end >= period_start");
        });
        builder.HasKey(i => i.Id);
        builder.Ignore(i => i.Events);
        builder.Property(i => i.Number).HasMaxLength(20);
        builder.HasIndex(i => i.Number).IsUnique();
        builder.Property(i => i.Reason).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property<string>("Currency").HasColumnType("char(3)").IsRequired().HasDefaultValue(Invoice.Currency);
        builder.Property(i => i.VoidReason).HasMaxLength(200);

        // Uma fatura por motivo e início de período: o job de renovação duplicado vira violação tratada.
        builder.HasIndex(i => new { i.SubscriptionId, i.PeriodStart, i.Reason }).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.IssuedAt, i.Number });
        builder.HasOne<Subscription>().WithMany().HasForeignKey(i => i.SubscriptionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Description).HasMaxLength(200);
        builder.HasIndex(l => new { l.InvoiceId, l.Position }).IsUnique();
        builder.HasOne<PlanVersion>().WithMany().HasForeignKey(l => l.PlanVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
