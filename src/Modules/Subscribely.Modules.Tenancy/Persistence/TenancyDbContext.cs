using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subscribely.Infrastructure.Persistence;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Tenancy.Domain;

namespace Subscribely.Modules.Tenancy.Persistence;

/// <summary>
/// Schema <c>tenancy</c>. Filtro global: quando há um tenant no contexto, só as linhas dele são visíveis.
/// Fluxos anônimos (login, signup) e de sistema/plataforma não têm tenant no contexto e enxergam tudo.
/// </summary>
internal sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options)
{
    public const string SchemaName = "tenancy";

    public override string Schema => SchemaName;

    private Guid? CurrentTenantId => tenantContext.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenancyDbContext).Assembly);

        modelBuilder.Entity<Tenant>().HasQueryFilter(t => CurrentTenantId == null || t.Id == CurrentTenantId);
        modelBuilder.Entity<User>().HasQueryFilter(u => CurrentTenantId == null || u.TenantId == CurrentTenantId);
        modelBuilder.Entity<Invitation>().HasQueryFilter(i => CurrentTenantId == null || i.TenantId == CurrentTenantId);
        modelBuilder.Entity<ApiKey>().HasQueryFilter(k => CurrentTenantId == null || k.TenantId == CurrentTenantId);
    }
}

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.Ignore(t => t.Events);
        builder.Property(t => t.Slug).HasMaxLength(40);
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Property(t => t.Name).HasMaxLength(100);
        builder.Property(t => t.DatabaseName).HasMaxLength(63);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.SignupPlanCode).HasMaxLength(30);
        builder.Property(t => t.SignupInterval).HasMaxLength(10);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.Ignore(u => u.Events);
        builder.Property(u => u.Email).HasMaxLength(254);
        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.TenantId);
        builder.Property(u => u.PasswordHash).HasMaxLength(200);
        builder.Property(u => u.Role).HasMaxLength(20);
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");
        builder.Ignore(i => i.Events);
        builder.Property(i => i.Email).HasMaxLength(254);
        builder.Property(i => i.Role).HasMaxLength(20);
        builder.Property(i => i.TokenHash).HasMaxLength(64);
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => i.TenantId);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.Ignore(k => k.Events);
        builder.Property(k => k.Name).HasMaxLength(60);
        builder.Property(k => k.KeyPrefix).HasMaxLength(8);
        builder.HasIndex(k => k.KeyPrefix).IsUnique();
        builder.Property(k => k.KeyHash).HasMaxLength(64);
        builder.HasIndex(k => k.TenantId);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(k => k.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");
        builder.Ignore(t => t.Events);
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
