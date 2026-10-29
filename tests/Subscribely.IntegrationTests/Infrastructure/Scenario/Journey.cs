using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Billing.Features.Subscriptions.Jobs;
using Subscribely.Modules.Payments.Features.Charges;

namespace Subscribely.IntegrationTests.Infrastructure.Scenario;

/// <summary>Planos dos cenários E2E (criados pela API de admin).</summary>
public static class Plans
{
    public const string Starter = "e2e-starter";
    public const string Pro = "e2e-pro";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Starter: R$ 49/mês, 100 req, 3 seats. Pro: R$ 149/mês, 1.000 req, 10 seats.</summary>
    public static async Task EnsureAsync(SubscribelyApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var admin = factory.CreateClient().AsPlatformAdmin();
        foreach (var (code, monthly, quota, seats) in new[] { (Starter, 4_900L, 100, 3), (Pro, 14_900L, 1_000, 10) })
        {
            var response = await admin.PostAsJsonAsync("/admin/plans", new
            {
                code,
                name = code,
                monthlyPriceCents = monthly,
                annualPriceCents = monthly * 10,
                requestQuotaMonthly = quota,
                maxSeats = seats,
            }, Ct);
            response.StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
        }
    }
}

/// <summary>
/// Um tenant percorrendo o produto só pela API HTTP. O tempo e o trabalho em background (jobs, outbox,
/// webhooks do gateway) avançam por <see cref="Clock"/>.
/// </summary>
public sealed class Journey
{
    private readonly SubscribelyApiFactory _factory;

    private Journey(SubscribelyApiFactory factory, TenantScenario tenant)
    {
        _factory = factory;
        Tenant = tenant;
    }

    public TenantScenario Tenant { get; }

    public string AdminEmail => Tenant.AdminEmail;

    /// <summary>TenantAdmin com token de longa duração (o relógio falso avança dias; o do login vive 15 min).</summary>
    public HttpClient Admin => _factory.CreateClient().WithBearer(TestTokens.Issue(Roles.TenantAdmin, Tenant.TenantId, slug: Tenant.Slug));

    public HttpClient? Product { get; private set; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<Journey> SignsUpAsync(SubscribelyApiFactory factory, string plan = Plans.Starter)
    {
        ArgumentNullException.ThrowIfNull(factory);
        await Plans.EnsureAsync(factory);
        return new Journey(factory, await Given.ActiveTenantAsync(factory, plan));
    }

    public async Task<Journey> RegistersCardAsync(string token)
    {
        (await Admin.PutAsJsonAsync("/v1/billing/payment-method", new { token }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return this;
    }

    public async Task<Journey> CreatesApiKeyAsync()
    {
        var created = await Admin.PostAsJsonAsync("/v1/api-keys", new { name = "e2e" }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        Product = _factory.CreateClient();
        Product.DefaultRequestHeaders.Add(ApiKeyDefaults.HeaderName, (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("key").GetString());
        return this;
    }

    public async Task<List<HttpResponseMessage>> CallsProductAsync(int times)
    {
        var product = Product ?? throw new InvalidOperationException("Create an API key first.");
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < times; i++)
        {
            responses.Add(await product.GetAsync(new Uri("/v1/projects", UriKind.Relative), Ct));
        }

        return responses;
    }

    public async Task<Guid> CreatesProjectAsync(string name)
    {
        var product = Product ?? throw new InvalidOperationException("Create an API key first.");
        var created = await product.PostAsJsonAsync("/v1/projects", new { name }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    public async Task<Guid> InvitesAsync(string email, string role = "Member")
    {
        var response = await Admin.PostAsJsonAsync("/v1/users/invitations", new { email, role }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    /// <summary>Convida e aceita: devolve o e-mail do novo Member ativo.</summary>
    public async Task<string> AddsMemberAsync()
    {
        var email = $"member-{Guid.NewGuid():N}@x.test";
        await InvitesAsync(email);
        var token = (await Given.OutboxEventAsync(_factory, "UserInvited", email)).GetProperty("token").GetString();
        (await _factory.CreateClient().PostAsJsonAsync($"/v1/invitations/{token}/accept", new { password = TenantScenario.DefaultPassword }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        return email;
    }

    public Task<HttpResponseMessage> ChangesPlanAsync(string planCode, string interval = "monthly") =>
        Admin.PostAsJsonAsync("/v1/subscription/change-plan", new { planCode, interval }, Ct);

    public Task<HttpResponseMessage> PostsAsync(string path) => Admin.PostAsync(new Uri(path, UriKind.Relative), null, Ct);

    public async Task<JsonElement> SubscriptionAsync() => await Admin.GetFromJsonAsync<JsonElement>("/v1/subscription", Ct);

    public async Task<string> StatusAsync() => (await SubscriptionAsync()).GetProperty("status").GetString()!;

    public async Task<List<JsonElement>> InvoicesAsync() =>
        (await Admin.GetFromJsonAsync<JsonElement>("/v1/invoices?limit=100", Ct)).GetProperty("items").EnumerateArray().ToList();

    public async Task<JsonElement> InvoiceAsync(Guid id) => await Admin.GetFromJsonAsync<JsonElement>($"/v1/invoices/{id}", Ct);
}

/// <summary>Relógio do cenário: avançar o tempo roda o que o sistema faria sozinho nesse intervalo.</summary>
public static class Clock
{
    /// <summary>Viaja até <paramref name="instant"/> e roda jobs, outbox e webhooks até estabilizar.</summary>
    public static async Task TravelToAsync(this SubscribelyApiFactory factory, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(factory);
        factory.Time.SetUtcNow(instant);
        await factory.SettleAsync();
    }

    public static Task AdvanceAsync(this SubscribelyApiFactory factory, TimeSpan delta)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.TravelToAsync(factory.Time.GetUtcNow() + delta);
    }

    /// <summary>Roda os jobs recorrentes, entrega os webhooks agendados do gateway e esvazia o outbox.</summary>
    public static async Task SettleAsync(this SubscribelyApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        for (var round = 0; round < 3; round++)
        {
            await factory.DrainOutboxAsync();
            await factory.RunJobAsync<TrialEndingNotifierJob>();
            await factory.RunJobAsync<TrialExpiryJob>();
            await factory.RunJobAsync<PeriodEndJob>();
            await factory.RunJobAsync<SuspensionExpiryJob>();
            await factory.RunJobAsync<DunningJob>();
            await factory.DrainOutboxAsync();
            await factory.Jobs.RunDueAsync(DateTimeOffset.UtcNow.AddSeconds(10));
        }

        await factory.DrainOutboxAsync();
        factory.Jobs.Failures.ShouldBeEmpty();
    }
}
