using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Subscribely.IntegrationTests.Infrastructure.ContainersFixture))]

namespace Subscribely.IntegrationTests.Infrastructure;

/// <summary>Containers compartilhados por toda a suíte (um Postgres, um Redis, um Mailpit).</summary>
public sealed class ContainersFixture : IAsyncLifetime
{
    public const int MailpitSmtpPort = 1025;
    public const int MailpitHttpPort = 8025;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("subscribely_catalog")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCommand("-c", "max_connections=1000")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.27")
        .WithPortBinding(MailpitSmtpPort, assignRandomHostPort: true)
        .WithPortBinding(MailpitHttpPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(MailpitHttpPort).ForPath("/livez")))
        .Build();

    public string CatalogConnectionString => _postgres.GetConnectionString();

    /// <summary>Cria um banco Catálogo exclusivo (isolamento total de outbox, Hangfire e auditoria por factory).</summary>
    public string CreateIsolatedCatalog()
    {
        var name = "catalog_" + Guid.NewGuid().ToString("N")[..12];
        using var connection = new Npgsql.NpgsqlConnection(AdminConnectionString);
        connection.Open();
#pragma warning disable CA2100 // nome gerado aqui (hex), sem entrada externa
        using var command = new Npgsql.NpgsqlCommand($"CREATE DATABASE {name}", connection);
#pragma warning restore CA2100
        command.ExecuteNonQuery();
        return new Npgsql.NpgsqlConnectionStringBuilder(CatalogConnectionString)
        {
            Database = name,
            MaxPoolSize = 20,
            ConnectionIdleLifetime = 15,
        }.ConnectionString;
    }

    /// <summary>Template de banco por tenant no mesmo servidor do container.</summary>
    public string TenantConnectionTemplate =>
        $"Host={_postgres.Hostname};Port={_postgres.GetMappedPublicPort(5432)};Database=subscribely_t_{{slug}};Username=postgres;Password=postgres;Maximum Pool Size=5;Connection Idle Lifetime=15";

    public string AdminConnectionString =>
        $"Host={_postgres.Hostname};Port={_postgres.GetMappedPublicPort(5432)};Database=postgres;Username=postgres;Password=postgres";

    public string RedisConnectionString => _redis.GetConnectionString();

    public string MailpitHost => _mailpit.Hostname;

    public int MailpitSmtpMappedPort => _mailpit.GetMappedPublicPort(MailpitSmtpPort);

    public Uri MailpitApiBaseAddress => new($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(MailpitHttpPort)}/");

    public async ValueTask InitializeAsync() =>
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _mailpit.StartAsync());

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await _mailpit.DisposeAsync();
    }
}
