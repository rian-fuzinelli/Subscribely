using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subscribely.Infrastructure.Tenancy;

namespace Subscribely.Infrastructure.Jobs;

public sealed class JobsOptions
{
    public const string Section = "Jobs";

    /// <summary>Desligado nos testes, que executam os jobs de forma síncrona.</summary>
    public bool ServerEnabled { get; set; } = true;

    public bool RecurringJobsEnabled { get; set; } = true;
}

/// <summary>Cada módulo registra seus jobs recorrentes implementando esta interface.</summary>
public interface IRecurringJobsRegistrar
{
    void Register(IRecurringJobManager manager);
}

public static class JobQueues
{
    public const string Critical = "critical";
    public const string Default = "default";
    public const string Low = "low";
}

public static class HangfireSetup
{
    public static IServiceCollection AddSubscribelyJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JobsOptions>(configuration.GetSection(JobsOptions.Section));

        services.AddHangfire((sp, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseLogProvider(new SafeHangfireLogProvider(sp.GetRequiredService<ILoggerFactory>()))
            .UsePostgreSqlStorage(
                o => o.UseNpgsqlConnection(sp.GetRequiredService<IConfiguration>().GetConnectionString("Catalog")),
                new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true, QueuePollInterval = TimeSpan.FromSeconds(1) }));

        services.AddHostedService<HangfireServerHost>();
        services.AddHostedService<RecurringJobsRegistration>();
        return services;
    }

    public static WebApplication MapSubscribelyJobsDashboard(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [new PlatformAdminDashboardFilter()],
            DisplayStorageConnectionString = false,
        });
        return app;
    }
}

/// <summary>Sobe o servidor do Hangfire só quando <c>Jobs:ServerEnabled</c> (decidido na subida, não no registro).</summary>
internal sealed class HangfireServerHost(IServiceProvider services, IOptions<JobsOptions> options) : IHostedService, IDisposable
{
    private BackgroundJobServer? _server;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.ServerEnabled)
        {
            _server = new BackgroundJobServer(
                new BackgroundJobServerOptions
                {
                    Queues = [JobQueues.Critical, JobQueues.Default, JobQueues.Low],
                    Activator = services.GetRequiredService<JobActivator>(),
                },
                services.GetRequiredService<JobStorage>());
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server is not null)
        {
            _server.SendStop();
            await _server.WaitForShutdownAsync(cancellationToken);
        }
    }

    public void Dispose() => _server?.Dispose();
}

/// <summary>
/// O Hangfire guarda o log provider em estado estático global. Com vários hosts no mesmo processo (testes),
/// um host descartado não pode derrubar o log dos demais: falhas de "objeto descartado" viram no-op.
/// </summary>
internal sealed class SafeHangfireLogProvider(ILoggerFactory loggerFactory) : Hangfire.Logging.ILogProvider
{
    private readonly Hangfire.AspNetCore.AspNetCoreLogProvider _inner = new(loggerFactory);

    public Hangfire.Logging.ILog GetLogger(string name)
    {
        try
        {
            return new SafeLog(_inner.GetLogger(name));
        }
        catch (ObjectDisposedException)
        {
            return new SafeLog(null);
        }
    }

    private sealed class SafeLog(Hangfire.Logging.ILog? inner) : Hangfire.Logging.ILog
    {
        public bool Log(Hangfire.Logging.LogLevel logLevel, Func<string>? messageFunc, Exception? exception = null)
        {
            try
            {
                return inner?.Log(logLevel, messageFunc, exception) ?? false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }
}

internal sealed class PlatformAdminDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        context.GetHttpContext().User.IsInRole(Roles.PlatformAdmin);
}

/// <summary>Registra os jobs recorrentes em background, com retry, para a API subir mesmo com o Postgres fora.</summary>
internal sealed partial class RecurringJobsRegistration(
    IServiceProvider services,
    IOptions<JobsOptions> options,
    ILogger<RecurringJobsRegistration> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RecurringJobsEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var manager = services.GetRequiredService<IRecurringJobManager>();
                foreach (var registrar in services.GetServices<IRecurringJobsRegistrar>())
                {
                    registrar.Register(manager);
                }

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRegistrationFailed(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recurring job registration failed; retrying in 10s")]
    private static partial void LogRegistrationFailed(ILogger logger, Exception exception);
}
