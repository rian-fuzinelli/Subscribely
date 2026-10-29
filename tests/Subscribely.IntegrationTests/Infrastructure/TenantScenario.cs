using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Outbox;

namespace Subscribely.IntegrationTests.Infrastructure;

public sealed record TokenPairDto(string AccessToken, int ExpiresIn, string RefreshToken);

/// <summary>Tenant criado pelo fluxo real (signup → outbox → provisionamento → login).</summary>
public sealed record TenantScenario(Guid TenantId, string Slug, string AdminEmail, string Password, TokenPairDto Tokens)
{
    public const string DefaultPassword = "Sup3r-Secret-Password!";

    public HttpClient AdminClient(SubscribelyApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.CreateClient().WithBearer(Tokens.AccessToken);
    }
}

public static class Given
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string NewSlug() => "t-" + Guid.NewGuid().ToString("N")[..12];

    public static async Task<HttpResponseMessage> SignUpAsync(
        SubscribelyApiFactory factory, string slug, string email, string plan = "starter", string interval = "monthly", string? password = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return await factory.CreateClient().PostAsJsonAsync("/v1/signup", new
        {
            companyName = $"Company {slug}",
            slug,
            adminEmail = email,
            password = password ?? TenantScenario.DefaultPassword,
            planCode = plan,
            interval,
        }, Ct);
    }

    /// <summary>Signup + provisionamento + login do admin. Garante que o plano exista (cria se preciso).</summary>
    public static async Task<TenantScenario> ActiveTenantAsync(SubscribelyApiFactory factory, string plan = "starter", string interval = "monthly")
    {
        ArgumentNullException.ThrowIfNull(factory);
        await EnsurePlanAsync(factory, plan);

        var slug = NewSlug();
        var email = $"admin@{slug}.test";
        var signup = await SignUpAsync(factory, slug, email, plan, interval);
        signup.StatusCode.ShouldBe(HttpStatusCode.Accepted, await signup.Content.ReadAsStringAsync(Ct));
        var tenantId = (await signup.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("tenantId").GetGuid();

        await factory.DrainOutboxAsync();

        var tokens = await LoginAsync(factory, email, TenantScenario.DefaultPassword);
        return new TenantScenario(tenantId, slug, email, TenantScenario.DefaultPassword, tokens);
    }

    public static async Task<TokenPairDto> LoginAsync(SubscribelyApiFactory factory, string email, string password)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var login = await factory.CreateClient().PostAsJsonAsync("/v1/auth/login", new { email, password }, Ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK, await login.Content.ReadAsStringAsync(Ct));
        return (await login.Content.ReadFromJsonAsync<TokenPairDto>(Ct))!;
    }

    public static async Task EnsurePlanAsync(SubscribelyApiFactory factory, string code, int maxSeats = 3, int quota = 1_000)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var response = await factory.CreateClient().AsPlatformAdmin().PostAsJsonAsync("/admin/plans", new
        {
            code,
            name = code,
            monthlyPriceCents = 4_900,
            annualPriceCents = 49_000,
            requestQuotaMonthly = quota,
            maxSeats,
        }, Ct);
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    /// <summary>Lê o payload de um evento ainda no outbox (ex.: o token de um convite, que só vai por e-mail).</summary>
    public static async Task<JsonElement> OutboxEventAsync(SubscribelyApiFactory factory, string eventTypeName, string containing)
    {
        var found = await FindOutboxEventAsync(factory, eventTypeName, containing);
        found.ShouldNotBeNull($"no {eventTypeName} event containing '{containing}' in the outbox");
        return found.Value;
    }

    public static async Task<JsonElement?> FindOutboxEventAsync(SubscribelyApiFactory factory, string eventTypeName, string containing)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var pattern = $"%{containing}%";
        var typePattern = $"%.{eventTypeName},%";
        var payload = await factory.WithDbAsync<OutboxDbContext, string?>(db => db.Messages
            .FromSql($"SELECT * FROM outbox.messages WHERE type LIKE {typePattern} AND payload::text LIKE {pattern} ORDER BY occurred_at DESC")
            .Select(m => m.Payload)
            .FirstOrDefaultAsync(Ct));
        return payload is null ? null : JsonDocument.Parse(payload).RootElement.Clone();
    }
}
