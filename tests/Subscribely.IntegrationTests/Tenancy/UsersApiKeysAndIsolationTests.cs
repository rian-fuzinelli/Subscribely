using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.Modules.Billing;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Persistence;

namespace Subscribely.IntegrationTests.Tenancy;

public sealed class UsersApiKeysAndIsolationTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers, configureServices: services =>
    {
        services.AddSingleton<IStartupFilter, WhoAmIEndpoint>();
        services.AddScoped<IBillingApi>(sp => new SeatLimitedBillingApi(new BillingApi(sp.GetRequiredService<BillingDbContext>())));
    });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Invite_WithinSeatLimit_ThenLimitReached_Returns409WithDetails()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        SeatLimitedBillingApi.MaxSeats[tenant.TenantId] = 2;
        var admin = tenant.AdminClient(_factory);

        (await Invite(admin, "first")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var blocked = await Invite(admin, "second");

        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("seat-limit-reached");
        problem.GetProperty("maxSeats").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task RevokedOrExpiredInvitation_FreesTheSeat()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        SeatLimitedBillingApi.MaxSeats[tenant.TenantId] = 2;
        var admin = tenant.AdminClient(_factory);

        var invitation = await (await Invite(admin, "temp")).Content.ReadFromJsonAsync<JsonElement>(Ct);
        (await admin.DeleteAsync(new Uri($"/v1/users/invitations/{invitation.GetProperty("id").GetGuid()}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Invite(admin, "after-revoke")).StatusCode.ShouldBe(HttpStatusCode.Created);

        _factory.Time.Advance(TimeSpan.FromHours(73));
        admin = _factory.CreateClient().WithBearer((await Given.LoginAsync(_factory, tenant.AdminEmail, tenant.Password)).AccessToken);
        (await Invite(admin, "after-expiry")).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AcceptInvitation_CreatesMember_WhoCannotManageUsers()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        SeatLimitedBillingApi.MaxSeats[tenant.TenantId] = 5;
        var email = $"member-{Guid.NewGuid():N}@x.test";
        (await tenant.AdminClient(_factory).PostAsJsonAsync("/v1/users/invitations", new { email, role = "Member" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var token = (await Given.OutboxEventAsync(_factory, "UserInvited", email)).GetProperty("token").GetString();

        var accepted = await _factory.CreateClient().PostAsJsonAsync($"/v1/invitations/{token}/accept", new { password = TenantScenario.DefaultPassword }, Ct);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await _factory.CreateClient().PostAsJsonAsync($"/v1/invitations/{token}/accept", new { password = TenantScenario.DefaultPassword }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Gone);

        var member = _factory.CreateClient().WithBearer((await Given.LoginAsync(_factory, email, TenantScenario.DefaultPassword)).AccessToken);
        (await member.GetAsync(new Uri("/v1/users", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Invite(member, "nope")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExpiredInvitation_Returns410()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        SeatLimitedBillingApi.MaxSeats[tenant.TenantId] = 5;
        var email = $"late-{Guid.NewGuid():N}@x.test";
        await tenant.AdminClient(_factory).PostAsJsonAsync("/v1/users/invitations", new { email, role = "Member" }, Ct);
        var token = (await Given.OutboxEventAsync(_factory, "UserInvited", email)).GetProperty("token").GetString();
        _factory.Time.Advance(TimeSpan.FromHours(73));

        (await _factory.CreateClient().PostAsJsonAsync($"/v1/invitations/{token}/accept", new { password = TenantScenario.DefaultPassword }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task DisableLastAdmin_Returns409()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        var admin = tenant.AdminClient(_factory);
        var users = await admin.GetFromJsonAsync<List<JsonElement>>("/v1/users", Ct);

        var response = await admin.PostAsync(new Uri($"/v1/users/{users!.Single().GetProperty("id").GetGuid()}/disable", UriKind.Relative), null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ApiKey_AuthenticatesTenant_AndRevocationIsImmediate()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        var admin = tenant.AdminClient(_factory);
        var created = await (await admin.PostAsJsonAsync("/v1/api-keys", new { name = "ci" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var key = created.GetProperty("key").GetString()!;
        key.ShouldStartWith("sk_live_");

        var whoAmI = await WithApiKey(key).GetFromJsonAsync<JsonElement>(WhoAmIEndpoint.Path, Ct);
        whoAmI.GetProperty("tenantId").GetGuid().ShouldBe(tenant.TenantId);
        whoAmI.GetProperty("actorType").GetString().ShouldBe("ApiKey");

        var listed = await admin.GetFromJsonAsync<List<JsonElement>>("/v1/api-keys", Ct);
        listed!.ShouldHaveSingleItem().TryGetProperty("key", out _).ShouldBeFalse("the secret is shown only once");

        (await admin.DeleteAsync(new Uri($"/v1/api-keys/{created.GetProperty("id").GetGuid()}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await WithApiKey(key).GetAsync(new Uri(WhoAmIEndpoint.Path, UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiKey_IsRejectedOnManagementEndpoints_AndInQueryString()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        var key = (await (await tenant.AdminClient(_factory).PostAsJsonAsync("/v1/api-keys", new { name = "x" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("key").GetString()!;

        (await WithApiKey(key).GetAsync(new Uri("/v1/tenant", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _factory.CreateClient().GetAsync(new Uri($"{WhoAmIEndpoint.Path}?api_key={key}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TenantB_CannotSeeOrTouchTenantA()
    {
        var a = await Given.ActiveTenantAsync(_factory);
        var b = await Given.ActiveTenantAsync(_factory);
        SeatLimitedBillingApi.MaxSeats[a.TenantId] = 5;
        var adminA = a.AdminClient(_factory);
        var adminB = b.AdminClient(_factory);

        var invitationA = (await (await Invite(adminA, "a-only")).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var keyA = (await (await adminA.PostAsJsonAsync("/v1/api-keys", new { name = "a" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var userA = (await adminA.GetFromJsonAsync<List<JsonElement>>("/v1/users", Ct))!.Single().GetProperty("id").GetGuid();

        (await adminB.GetFromJsonAsync<List<JsonElement>>("/v1/users", Ct))!.ShouldAllBe(u => u.GetProperty("email").GetString() == b.AdminEmail);
        (await adminB.GetFromJsonAsync<List<JsonElement>>("/v1/users/invitations", Ct))!.ShouldBeEmpty();
        (await adminB.GetFromJsonAsync<List<JsonElement>>("/v1/api-keys", Ct))!.ShouldBeEmpty();
        (await adminB.GetFromJsonAsync<JsonElement>("/v1/tenant", Ct)).GetProperty("id").GetGuid().ShouldBe(b.TenantId);
        (await adminB.DeleteAsync(new Uri($"/v1/users/invitations/{invitationA}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await adminB.DeleteAsync(new Uri($"/v1/api-keys/{keyA}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await adminB.PostAsync(new Uri($"/v1/users/{userA}/disable", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await adminB.PostAsync(new Uri($"/v1/users/{userA}/enable", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminTenants_RequiresPlatformAdmin()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);

        (await tenant.AdminClient(_factory).GetAsync(new Uri("/admin/tenants", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var all = await _factory.CreateClient().AsPlatformAdmin().GetFromJsonAsync<List<JsonElement>>("/admin/tenants", Ct);
        all!.ShouldContain(t => t.GetProperty("id").GetGuid() == tenant.TenantId);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private Task<HttpResponseMessage> Invite(HttpClient client, string name) =>
        client.PostAsJsonAsync("/v1/users/invitations", new { email = $"{name}-{Guid.NewGuid():N}@x.test", role = "Member" }, Ct);

    private HttpClient WithApiKey(string key)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyDefaults.HeaderName, key);
        return client;
    }

    /// <summary>Até a spec 03 não há assinatura; este decorator fixa o limite de assentos por tenant.</summary>
    private sealed class SeatLimitedBillingApi(IBillingApi inner) : IBillingApi
    {
        public static ConcurrentDictionary<Guid, int> MaxSeats { get; } = new();

        public Task<PlanVersionInfo?> GetActivePlanVersionAsync(string planCode, CancellationToken cancellationToken) =>
            inner.GetActivePlanVersionAsync(planCode, cancellationToken);

        public Task<Entitlements?> GetEntitlementsAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(MaxSeats.TryGetValue(tenantId, out var seats) ? new Entitlements("Trialing", 1_000, seats, true) : null);
    }

    /// <summary>Endpoint de teste protegido pela política de API key (o produto real chega na spec 06).</summary>
    private sealed class WhoAmIEndpoint : IStartupFilter
    {
        public const string Path = "/_test/whoami";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(e => e.MapGet(Path, (ITenantContext t) => Results.Ok(new { t.TenantId, ActorType = t.ActorType.ToString(), t.ActorId }))
                .RequireAuthorization(Policies.ApiKey));
        };
    }
}
