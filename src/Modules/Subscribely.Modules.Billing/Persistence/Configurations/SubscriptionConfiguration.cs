using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Domain.Subscriptions;

namespace Subscribely.Modules.Billing.Persistence.Configurations;

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(s => s.Id);
        builder.Ignore(s => s.Events);
        builder.Ignore(s => s.CurrentPeriod);
        builder.Ignore(s => s.ProductAccessAllowed);
        builder.Ignore(s => s.HasPendingChange);

        builder.HasIndex(s => s.TenantId).IsUnique();
        builder.HasIndex(s => new { s.Status, s.CurrentPeriodEnd });
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Interval).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.PendingInterval).HasConversion<string>().HasMaxLength(10);

        builder.HasOne<PlanVersion>().WithMany().HasForeignKey(s => s.PlanVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanVersion>().WithMany().HasForeignKey(s => s.PendingPlanVersionId).OnDelete(DeleteBehavior.Restrict);

        // Coluna de sistema do Postgres: muda a cada UPDATE, sem coluna extra na tabela.
        builder.Property(s => s.Version).IsRowVersion();
    }
}
