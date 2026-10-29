using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Subscribely.Infrastructure.Outbox;

namespace Subscribely.IntegrationTests.Infrastructure;

/// <summary>
/// Sobe a API real contra os containers. Por padrão: migrations na subida, jobs do Hangfire executados de
/// forma síncrona (<see cref="SyncJobClient"/>), outbox processado explicitamente e <see cref="FakeTimeProvider"/>.
/// </summary>
public class SubscribelyApiFactory(
    ContainersFixture containers,
    Action<IDictionary<string, string?>>? configureSettings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset DefaultStart = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public FakeTimeProvider Time { get; } = new(DefaultStart);

    public SyncJobClient Jobs => (SyncJobClient)Services.GetRequiredService<IBackgroundJobClient>();

    /// <summary>Processa o outbox até esvaziar (os handlers podem gerar novos eventos e jobs síncronos).</summary>
    public async Task DrainOutboxAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().DrainAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Executa uma ação com um DbContext num escopo novo.</summary>
    public async Task<T> WithDbAsync<TContext, T>(Func<TContext, Task<T>> action)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = containers.CreateIsolatedCatalog(),
            ["ConnectionStrings:PostgresAdmin"] = containers.AdminConnectionString,
            ["ConnectionStrings:Redis"] = containers.RedisConnectionString,
            ["TenantDatabase:ConnectionTemplate"] = containers.TenantConnectionTemplate,
            ["Database:MigrateOnStartup"] = "true",
            ["Jwt:SigningKey"] = TestTokens.SigningKey,
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Hangfire"] = "None",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "None",
            ["Outbox:DispatcherEnabled"] = "false",
            ["Jobs:ServerEnabled"] = "false",
            ["Jobs:RecurringJobsEnabled"] = "false",
            ["Smtp:Host"] = containers.MailpitHost,
            ["Smtp:Port"] = containers.MailpitSmtpMappedPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        configureSettings?.Invoke(settings);
        var realJobs = settings.TryGetValue("Jobs:ServerEnabled", out var server) && server == "true";

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            if (!realJobs)
            {
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton<IBackgroundJobClient, SyncJobClient>();
            }

            configureServices?.Invoke(services);
        });
    }
}
