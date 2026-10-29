using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using Subscribely.Infrastructure.Auditing;
using Subscribely.Infrastructure.Jobs;
using Subscribely.Infrastructure.Outbox;
using Subscribely.Infrastructure.Persistence;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Infrastructure.Web;

namespace Subscribely.Infrastructure;

public static class InfrastructureSetup
{
    public const string CatalogConnectionName = "Catalog";
    public const string RedisConnectionName = "Redis";
    private const string ReadyTag = "ready";

    public static IServiceCollection AddSubscribelyInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.Configure<TenantDatabaseOptions>(configuration.GetSection(TenantDatabaseOptions.Section));
        services.AddSingleton<TenantDatabaseFactory>();

        // Migrações primeiro: hosted services sobem na ordem de registro.
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Section));
        services.AddSingleton<DatabaseMigrator>();
        services.AddHostedService<DatabaseMigrationHostedService>();

        services.AddHttpContextAccessor();
        services.AddScoped<AuditActorAccessor>();
        services.AddScoped<AuditBuffer>();
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<IAuditLog, AuditLog>();

        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.Section));
        services.AddSingleton<OutboxInterceptor>();
        services.AddModuleDbContext<OutboxDbContext>(OutboxDbContext.SchemaName);
        services.AddScoped<OutboxProcessor>();
        services.AddHostedService<OutboxDispatcher>();

        // Configuração lida de forma tardia: o WebApplicationFactory sobrescreve valores só depois do registro.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = ConfigurationOptions.Parse(RedisConnectionString(sp.GetRequiredService<IConfiguration>()));
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 2000;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddHostedService<RedisWarmup>();
        services.AddSingleton<Caching.RedisRateLimiter>();
        services.AddStackExchangeRedisCache(_ => { });
        services.AddOptions<Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions>()
            .Configure<IConnectionMultiplexer>((o, mux) => o.ConnectionMultiplexerFactory = () => Task.FromResult(mux));
        services.AddHybridCache();

        services.AddSubscribelyJobs(configuration);

        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);

        services.AddHealthChecks()
            .AddNpgSql(
                sp => sp.GetRequiredService<IConfiguration>().GetConnectionString(CatalogConnectionName)!,
                name: "postgres",
                tags: [ReadyTag],
                timeout: TimeSpan.FromMilliseconds(800))
            .AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                name: "redis",
                tags: [ReadyTag],
                timeout: TimeSpan.FromMilliseconds(800));

        return services;
    }

    private static string RedisConnectionString(IConfiguration configuration)
    {
        var value = configuration.GetConnectionString(RedisConnectionName);
        return string.IsNullOrWhiteSpace(value) ? "localhost:6379" : value;
    }

    /// <summary>Registra o DbContext de um módulo no banco Catálogo (schema próprio, snake_case, outbox).</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>((sp, options) => options
            .UseNpgsql(
                sp.GetRequiredService<IConfiguration>().GetConnectionString(CatalogConnectionName),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<OutboxInterceptor>(), sp.GetRequiredService<AuditInterceptor>()));

        services.AddSingleton(new ModuleDbContextRegistration(typeof(TContext)));
        return services;
    }

    /// <summary>Middlewares transversais que vêm antes da autenticação.</summary>
    public static WebApplication UseSubscribelyInfrastructure(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    /// <summary>Preenche o <see cref="ITenantContext"/>; chamar entre UseAuthentication e UseAuthorization.</summary>
    public static WebApplication UseTenantResolution(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<TenantResolutionMiddleware>();
        return app;
    }

    public static WebApplication MapSubscribelyHealthChecks(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteHealthJsonAsync,
            ResultStatusCodes =
            {
                [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
                [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).AllowAnonymous();
        return app;
    }

    /// <summary>Status por dependência, sem mensagens de exceção (não vaza detalhes de infraestrutura).</summary>
    private static Task WriteHealthJsonAsync(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        };
        return context.Response.WriteAsJsonAsync(payload);
    }
}

/// <summary>Abre a conexão com o Redis na subida, para o primeiro uso (e o /health/ready) não pagar o handshake.</summary>
internal sealed class RedisWarmup(IServiceProvider services) : Microsoft.Extensions.Hosting.IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        Task.Run(() => services.GetRequiredService<IConnectionMultiplexer>(), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
