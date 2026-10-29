using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subscribely.Infrastructure.Persistence;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Domain.Subscriptions;

namespace Subscribely.Modules.Billing.Persistence;

internal sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "billing";

    public override string Schema => SchemaName;

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<PlanVersion> PlanVersions => Set<PlanVersion>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
    }
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");
        builder.HasKey(p => p.Id);
        builder.Ignore(p => p.Events);
        builder.Ignore(p => p.CurrentVersion);
        builder.Property(p => p.Code).HasMaxLength(30);
        builder.HasIndex(p => p.Code).IsUnique();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasMany(p => p.Versions).WithOne().HasForeignKey(v => v.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(p => p.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PlanVersionConfiguration : IEntityTypeConfiguration<PlanVersion>
{
    public void Configure(EntityTypeBuilder<PlanVersion> builder)
    {
        builder.ToTable("plan_versions", t =>
        {
            t.HasCheckConstraint("ck_plan_versions_prices", "monthly_price_cents >= 0 AND annual_price_cents >= 0");
            t.HasCheckConstraint("ck_plan_versions_limits", "request_quota_monthly > 0 AND max_seats > 0");
        });
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Name).HasMaxLength(60);
        builder.HasIndex(v => new { v.PlanId, v.Version }).IsUnique();
    }
}
