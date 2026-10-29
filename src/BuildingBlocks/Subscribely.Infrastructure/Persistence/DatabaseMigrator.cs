using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Subscribely.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Aplica as migrations do Catálogo na inicialização (Development, compose e testes).</summary>
    public bool MigrateOnStartup { get; set; }
}

internal sealed record ModuleDbContextRegistration(Type ContextType);

/// <summary>Aplica as migrations de todos os contextos de módulo registrados no banco Catálogo.</summary>
public sealed partial class DatabaseMigrator(IServiceProvider services, ILogger<DatabaseMigrator> logger)
{
    public async Task MigrateCatalogAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        foreach (var registration in scope.ServiceProvider.GetServices<ModuleDbContextRegistration>())
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);
            await context.Database.MigrateAsync(cancellationToken);
            LogMigrated(logger, registration.ContextType.Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog migrations applied for {Context}")]
    private static partial void LogMigrated(ILogger logger, string context);
}

/// <summary>Executa a migração antes de a aplicação começar a aceitar requisições.</summary>
internal sealed class DatabaseMigrationHostedService(DatabaseMigrator migrator, IOptions<DatabaseOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        options.Value.MigrateOnStartup ? migrator.MigrateCatalogAsync(cancellationToken) : Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
