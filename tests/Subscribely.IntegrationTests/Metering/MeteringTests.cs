using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.Modules.Metering.Contracts;
using Subscribely.Modules.Metering.Features;

namespace Subscribely.IntegrationTests.Metering;

public sealed class MeteringTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Projects_Crud_WithApiKey_PersistsInTheTenantDatabase()
    {
        var (tenant, product) = await TenantWithApiKeyAsync("meter-crud", quota: 1_000);

        var created = await product.PostAsJsonAsync("/v1/projects", new { name = "Site", description = "landing" }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        (await product.PutAsJsonAsync($"/v1/projects/{id}", new { name = "Site v2" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await product.GetFromJsonAsync<JsonElement>($"/v1/projects/{id}", Ct)).GetProperty("name").GetString().ShouldBe("Site v2");
        (await product.GetFromJsonAsync<List<JsonElement>>("/v1/projects", Ct))!.ShouldHaveSingleItem();
        (await product.GetAsync(new Uri($"/v1/projects/{Guid.NewGuid()}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await product.PostAsJsonAsync("/v1/projects", new { name = "" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await product.DeleteAsync(new Uri($"/v1/projects/{id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var connection = new NpgsqlConnection(containers.TenantConnectionTemplate.Replace("{slug}", tenant.Slug.Replace('-', '_'), StringComparison.Ordinal));
        await connection.OpenAsync(Ct);
        await using var audit = new NpgsqlCommand("SELECT count(*) FROM public.audit_entries WHERE entity_type LIKE '%Project%'", connection);
        ((long)(await audit.ExecuteScalarAsync(Ct))!).ShouldBe(3, "create, update and delete are audited in the tenant database");
    }

    [Fact]
    public async Task Projects_RejectJwt_AndTheProductAuditIsReadable()
    {
        var (tenant, product) = await TenantWithApiKeyAsync("meter-jwt", quota: 1_000);
        await product.PostAsJsonAsync("/v1/projects", new { name = "Audited" }, Ct);
        var admin = tenant.AdminClient(_factory); // token do login: traz o slug do tenant (banco do data plane)

        (await admin.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var audit = await admin.GetFromJsonAsync<JsonElement>("/v1/audit?source=product", Ct);
        audit.GetProperty("items").EnumerateArray().ShouldContain(e => e.GetProperty("actorType").GetString() == "ApiKey");
    }

    [Fact]
    public async Task EveryProductResponse_HasRateLimitHeaders()
    {
        var (_, product) = await TenantWithApiKeyAsync("meter-headers", quota: 50);

        var response = await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct);

        response.Headers.GetValues("X-RateLimit-Limit").Single().ShouldBe("50");
        response.Headers.GetValues("X-RateLimit-Remaining").Single().ShouldBe("49");
        var reset = long.Parse(response.Headers.GetValues("X-RateLimit-Reset").Single(), System.Globalization.CultureInfo.InvariantCulture);
        reset.ShouldBe(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task ConcurrentRequests_200WithLimit100_ExactlyHalfSucceed()
    {
        var (tenant, product) = await TenantWithApiKeyAsync("meter-load", quota: 100);

        var responses = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(100);
        var rejected = responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        rejected.Count.ShouldBe(100);
        var retryAfter = rejected[0].Headers.RetryAfter!.Delta!.Value;
        retryAfter.ShouldBe(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero) - _factory.Time.GetUtcNow(), TimeSpan.FromSeconds(1));
        (await rejected[0].Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("quota-exceeded");
        (await UsageAsync(tenant)).GetProperty("requestsUsed").GetInt64().ShouldBe(100, "rejected requests do not increment");
    }

    [Fact]
    public async Task InactiveSubscription_Is402_WithoutConsumingQuota()
    {
        var (tenant, product) = await TenantWithApiKeyAsync("meter-402", quota: 100);
        await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct);

        _factory.Time.Advance(TimeSpan.FromDays(14));
        await _factory.RunJobAsync<Modules.Billing.Features.Subscriptions.Jobs.TrialExpiryJob>();

        var response = await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("subscription-inactive");
        (await UsageAsync(tenant)).GetProperty("requestsUsed").GetInt64().ShouldBe(1);
    }

    [Fact]
    public async Task Thresholds_80And100_ArePublishedOncePerPeriod()
    {
        var (tenant, product) = await TenantWithApiKeyAsync("meter-alert", quota: 10);

        for (var i = 0; i < 13; i++)
        {
            await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct);
        }

        var payloads = await OutboxPayloadsAsync(nameof(QuotaThresholdReached), tenant.TenantId);
        payloads.Select(p => p.GetProperty("threshold").GetInt32()).Order().ShouldBe([80, 100]);
        payloads.ShouldAllBe(p => p.GetProperty("period").GetString() == "2026-01");
    }

    [Fact]
    public async Task Upgrade_MidMonth_RaisesTheLimitOnTheNextRequest_KeepingTheCounter()
    {
        await Given.EnsurePlanAsync(_factory, "meter-big", quota: 500);
        var (tenant, product) = await TenantWithApiKeyAsync("meter-small", quota: 2);
        await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct);
        await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct);
        (await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        (await (await AdminAsync(tenant)).PostAsJsonAsync("/v1/subscription/change-plan", new { planCode = "meter-big", interval = "monthly" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-RateLimit-Remaining").Single().ShouldBe("497");
    }

    [Fact]
    public async Task Usage_ShowsCounterAndSeats_AndTheSnapshotFeedsTheHistory()
    {
        var (tenant, product) = await TenantWithApiKeyAsync("meter-usage", quota: 100);
        for (var i = 0; i < 3; i++)
        {
            await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct);
        }

        var usage = await UsageAsync(tenant);
        usage.GetProperty("period").GetString().ShouldBe("2026-01");
        usage.GetProperty("requestsUsed").GetInt64().ShouldBe(3);
        usage.GetProperty("requestLimit").GetInt64().ShouldBe(100);
        usage.GetProperty("seatsUsed").GetInt32().ShouldBe(1);
        usage.GetProperty("seatLimit").GetInt32().ShouldBe(3);

        (await _factory.RunJobAsync<UsageSnapshotJob>()).ShouldBeGreaterThan(0);
        await _factory.RunJobAsync<UsageSnapshotJob>();

        var member = _factory.CreateClient().WithBearer(TestTokens.Issue(Roles.Member, tenant.TenantId));
        var history = await member.GetFromJsonAsync<List<JsonElement>>("/v1/usage/history", Ct);
        history!.ShouldHaveSingleItem().GetProperty("requestsCount").GetInt64().ShouldBe(3);
    }

    [Fact]
    public async Task StartupMigration_BringsExistingTenantDatabasesUpToDate()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        await using (var connection = new NpgsqlConnection(containers.TenantConnectionTemplate.Replace("{slug}", tenant.Slug.Replace('-', '_'), StringComparison.Ordinal)))
        {
            await connection.OpenAsync(Ct);
            await using var drop = new NpgsqlCommand("DROP TABLE projects; DROP TABLE public.audit_entries; DELETE FROM \"__EFMigrationsHistory\";", connection);
            await drop.ExecuteNonQueryAsync(Ct);
        }

        foreach (var hosted in _factory.Services.GetServices<IHostedService>().Where(s => s.GetType().Name == "TenantDatabasesMigrationHostedService"))
        {
            await hosted.StartAsync(Ct);
        }

        var product = await ApiKeyClientAsync(tenant);
        (await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private Task<HttpClient> AdminAsync(TenantScenario tenant) =>
        Task.FromResult(_factory.CreateClient().WithBearer(TestTokens.Issue(Roles.TenantAdmin, tenant.TenantId)));

    private async Task<(TenantScenario Tenant, HttpClient Product)> TenantWithApiKeyAsync(string plan, int quota)
    {
        await Given.EnsurePlanAsync(_factory, plan, quota: quota);
        var tenant = await Given.ActiveTenantAsync(_factory, plan);
        return (tenant, await ApiKeyClientAsync(tenant));
    }

    private async Task<HttpClient> ApiKeyClientAsync(TenantScenario tenant)
    {
        var created = await tenant.AdminClient(_factory).PostAsJsonAsync("/v1/api-keys", new { name = "product" }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyDefaults.HeaderName, (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("key").GetString());
        return client;
    }

    private async Task<JsonElement> UsageAsync(TenantScenario tenant) =>
        await (await AdminAsync(tenant)).GetFromJsonAsync<JsonElement>("/v1/usage", Ct);

    private async Task<List<JsonElement>> OutboxPayloadsAsync(string eventType, Guid tenantId)
    {
        var typePattern = $"%.{eventType},%";
        var pattern = $"%{tenantId}%";
        var payloads = await _factory.WithDbAsync<Subscribely.Infrastructure.Outbox.OutboxDbContext, List<string>>(db => db.Messages
            .FromSql($"SELECT * FROM outbox.messages WHERE type LIKE {typePattern} AND payload::text LIKE {pattern}")
            .Select(m => m.Payload)
            .ToListAsync(Ct));
        return payloads.Select(p => JsonDocument.Parse(p).RootElement.Clone()).ToList();
    }
}

/// <summary>Redis inacessível: o produto continua respondendo (fail-open) e um warning é registrado.</summary>
public sealed class MeteringFailOpenTests(ContainersFixture containers) : IAsyncLifetime
{
    private static readonly ConcurrentQueue<string> Warnings = new();

    private readonly SubscribelyApiFactory _factory = new(
        containers,
        settings => settings["ConnectionStrings:Redis"] = "127.0.0.1:1,connectTimeout=200,syncTimeout=300,asyncTimeout=300",
        services => services.AddSingleton<ILoggerProvider>(new CollectingLoggerProvider(Warnings)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RedisDown_ProductRequestPasses_AndAWarningIsLogged()
    {
        await Given.EnsurePlanAsync(_factory, "meter-failopen", quota: 1);
        var tenant = await Given.ActiveTenantAsync(_factory, "meter-failopen");
        var created = await tenant.AdminClient(_factory).PostAsJsonAsync("/v1/api-keys", new { name = "p" }, Ct);
        var product = _factory.CreateClient();
        product.DefaultRequestHeaders.Add(ApiKeyDefaults.HeaderName, (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("key").GetString());

        (await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, "the quota is not enforced while Redis is down");

        Warnings.ShouldContain(w => w.Contains("Quota check unavailable", StringComparison.Ordinal));
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private sealed class CollectingLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Collector(sink);

        public void Dispose()
        {
        }

        private sealed class Collector(ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    sink.Enqueue(formatter(state, exception));
                }
            }
        }
    }
}
