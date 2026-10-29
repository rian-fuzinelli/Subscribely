using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subscribely.Infrastructure.Auditing;
using Subscribely.Infrastructure.Persistence;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Audit.Contracts;
using Subscribely.Modules.Projects.Domain;
using Subscribely.Modules.Tenancy.Contracts;

namespace Subscribely.Modules.Projects.Persistence;

/// <summary>
/// Data plane: banco do tenant da requisição atual (sem coluna <c>tenant_id</c>; o isolamento é físico).
/// A auditoria do produto vai para <c>public.audit_entries</c> no mesmo banco.
/// </summary>
internal sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>Opções para o banco de um tenant específico (requisição, provisionamento ou migração em lote).</summary>
    public static DbContextOptions<ProjectsDbContext> OptionsFor(string connectionString, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<ProjectsDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptors)
            .Options;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(AuditEntryConfiguration.DataPlane());
        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
    }
}

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(Project.MaxNameLength);
        builder.HasIndex(p => p.CreatedAt);
    }
}

/// <summary>Só para o <c>dotnet ef</c> gerar as migrations do banco do tenant.</summary>
internal sealed class ProjectsDesignTimeFactory : IDesignTimeDbContextFactory<ProjectsDbContext>
{
    public ProjectsDbContext CreateDbContext(string[] args) =>
        new(ProjectsDbContext.OptionsFor("Host=localhost;Database=subscribely_t_design"));
}

/// <summary>Aplica as migrations do data plane no banco de um tenant (chamado pelo provisionamento).</summary>
internal sealed class ProjectsDatabaseMigrator(TenantDatabaseFactory databases) : ITenantDatabaseMigrator
{
    public async Task MigrateAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        await using var db = new ProjectsDbContext(ProjectsDbContext.OptionsFor(databases.ConnectionStringFor(tenantSlug)));
        await db.Database.MigrateAsync(cancellationToken);
    }
}

/// <summary>Na subida (com <c>Database:MigrateOnStartup</c>), leva todos os tenants existentes à última migration.</summary>
internal sealed partial class TenantDatabasesMigrationHostedService(
    IServiceProvider services,
    IOptions<DatabaseOptions> options,
    ILogger<TenantDatabasesMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MigrateOnStartup)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<ITenantDatabaseMigrator>();
        foreach (var slug in await scope.ServiceProvider.GetRequiredService<ITenancyApi>().ListActiveTenantSlugsAsync(cancellationToken))
        {
            try
            {
                await migrator.MigrateAsync(slug, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTenantMigrationFailed(logger, ex, slug);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Error, Message = "Data plane migration failed for tenant {TenantSlug}")]
    private static partial void LogTenantMigrationFailed(ILogger logger, Exception exception, string tenantSlug);
}

/// <summary>Trilha do produto lida do banco do tenant atual (<c>/v1/audit?source=product</c>).</summary>
internal sealed class ProductAuditSource(ProjectsDbContext db) : IProductAuditSource
{
    public IQueryable<AuditEntry> Entries => db.AuditEntries;
}
