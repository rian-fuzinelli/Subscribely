using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Subscribely.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Aplica as migrations do Catálogo na inicialização (Development, compose e testes).</summary>
    public bool MigrateOnStartup { get; set; }
}

internal sealed record ModuleDbContextRegistration(Type ContextType);

/// <summary>
/// Aplica as migrations de todos os contextos de módulo e o schema do Hangfire no Catálogo, sob um
/// advisory lock do Postgres (várias instâncias subindo ao mesmo tempo não disputam o DDL).
/// </summary>
public sealed partial class DatabaseMigrator(IServiceProvider services, IConfiguration configuration, ILogger<DatabaseMigrator> logger)
{
    private const long MigrationLockKey = 0x5355_4253_4352_4942; // "SUBSCRIB"

    public async Task MigrateCatalogAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString(InfrastructureSetup.CatalogConnectionName));
        await connection.OpenAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand($"SELECT pg_advisory_lock({MigrationLockKey})", connection))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            PostgreSqlObjectsInstaller.Install(connection, "hangfire");

            await using var scope = services.CreateAsyncScope();
            foreach (var registration in scope.ServiceProvider.GetServices<ModuleDbContextRegistration>())
            {
                var context = (DbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);
                await context.Database.MigrateAsync(cancellationToken);
                LogMigrated(logger, registration.ContextType.Name);
            }
        }
        finally
        {
            await using var unlockCommand = new NpgsqlCommand($"SELECT pg_advisory_unlock({MigrationLockKey})", connection);
            await unlockCommand.ExecuteNonQueryAsync(CancellationToken.None);
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
