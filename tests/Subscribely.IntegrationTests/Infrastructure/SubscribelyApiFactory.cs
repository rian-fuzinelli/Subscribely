using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Subscribely.IntegrationTests.Infrastructure;

/// <summary>
/// Sobe a API real contra os containers. Por padrão: migrations na subida, sem servidor Hangfire,
/// sem dispatcher do outbox (os testes processam de forma explícita) e com <see cref="FakeTimeProvider"/>.
/// </summary>
public class SubscribelyApiFactory(
    ContainersFixture containers,
    Action<IDictionary<string, string?>>? configureSettings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset DefaultStart = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public FakeTimeProvider Time { get; } = new(DefaultStart);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = containers.CatalogConnectionString,
            ["ConnectionStrings:PostgresAdmin"] = containers.AdminConnectionString,
            ["ConnectionStrings:Redis"] = containers.RedisConnectionString,
            ["TenantDatabase:ConnectionTemplate"] = containers.TenantConnectionTemplate,
            ["Database:MigrateOnStartup"] = "true",
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

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            configureServices?.Invoke(services);
        });
    }
}
