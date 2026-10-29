using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subscribely.Infrastructure.Persistence;

namespace Subscribely.Modules.Metering.Persistence;

/// <summary>Histórico do contador mensal (o caminho quente fica no Redis; isto é só o snapshot por hora).</summary>
internal sealed class UsagePeriod
{
    public Guid TenantId { get; set; }

    /// <summary><c>yyyy-MM</c> (UTC).</summary>
    public string Period { get; set; } = string.Empty;

    public long RequestsCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class MeteringDbContext(DbContextOptions<MeteringDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "metering";

    public override string Schema => SchemaName;

    public DbSet<UsagePeriod> UsagePeriods => Set<UsagePeriod>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new UsagePeriodConfiguration());
    }
}

internal sealed class UsagePeriodConfiguration : IEntityTypeConfiguration<UsagePeriod>
{
    public void Configure(EntityTypeBuilder<UsagePeriod> builder)
    {
        builder.ToTable("usage_periods", t => t.HasCheckConstraint("ck_usage_periods_count", "requests_count >= 0"));
        builder.HasKey(u => new { u.TenantId, u.Period });
        builder.Property(u => u.Period).HasColumnType("char(7)");
    }
}
