using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Subscribely.Infrastructure.Outbox;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Domain.Subscriptions;
using Subscribely.Modules.Billing.Features.Subscriptions;
using Subscribely.Modules.Billing.Features.Subscriptions.Jobs;
using Subscribely.Modules.Billing.Persistence;
using Subscribely.Modules.Payments.Contracts;

namespace Subscribely.IntegrationTests.Billing;

public sealed class SubscriptionLifecycleTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers, configureServices: services =>
    {
        services.RemoveAll<IPaymentsApi>();
        services.AddSingleton<IPaymentsApi, FakePaymentsApi>();
    });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Signup_StartsA14DayTrial_AndPublishesSubscriptionStarted()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);

        var subscription = await GetSubscriptionAsync(tenant);

        subscription.GetProperty("status").GetString().ShouldBe("Trialing");
        subscription.GetProperty("plan").GetProperty("code").GetString().ShouldBe("starter");
        subscription.GetProperty("interval").GetString().ShouldBe("monthly");
        subscription.GetProperty("trialEndsAt").GetDateTimeOffset().ShouldBe(_factory.Time.GetUtcNow().AddDays(14));
        (await Given.FindOutboxEventAsync(_factory, nameof(SubscriptionStarted), tenant.TenantId.ToString())).ShouldNotBeNull();
    }

    [Fact]
    public async Task TenantProvisioned_DeliveredTwice_CreatesASingleSubscription()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<Modules.Tenancy.Contracts.TenantProvisioned>>();
            await handler.HandleAsync(new Modules.Tenancy.Contracts.TenantProvisioned(tenant.TenantId, "starter", "monthly"), Ct);
        }

        (await _factory.WithDbAsync<BillingDbContext, int>(db => db.Subscriptions.CountAsync(s => s.TenantId == tenant.TenantId, Ct))).ShouldBe(1);
    }

    [Fact]
    public async Task TrialEnd_WithoutPaymentMethod_Expires_AndEntitlementsBlockTheProduct()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        await EntitlementsAsync(tenant.TenantId); // aquece o cache

        _factory.Time.Advance(TimeSpan.FromDays(14));
        (await _factory.RunJobAsync<TrialExpiryJob>()).ShouldBe(1);
        (await _factory.RunJobAsync<TrialExpiryJob>()).ShouldBe(0);

        (await GetSubscriptionAsync(tenant)).GetProperty("status").GetString().ShouldBe("Expired");
        (await EntitlementsAsync(tenant.TenantId))!.ProductAccessAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task TrialEnd_WithPaymentMethod_ConvertsToActive_WithANewMonthlyPeriod()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        FakePaymentsApi.WithPaymentMethod[tenant.TenantId] = true;
        var trialEndsAt = _factory.Time.GetUtcNow().AddDays(14);

        _factory.Time.Advance(TimeSpan.FromDays(14).Add(TimeSpan.FromMinutes(20)));
        await _factory.RunJobAsync<TrialExpiryJob>();

        var subscription = await GetSubscriptionAsync(tenant);
        subscription.GetProperty("status").GetString().ShouldBe("Active");
        subscription.GetProperty("currentPeriodStart").GetDateTimeOffset().ShouldBe(trialEndsAt);
        subscription.GetProperty("currentPeriodEnd").GetDateTimeOffset().ShouldBe(trialEndsAt.AddMonths(1));
    }

    [Fact]
    public async Task TrialEndingNotifier_PublishesOncePerSubscription()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);

        _factory.Time.Advance(TimeSpan.FromDays(10));
        await _factory.RunJobAsync<TrialEndingNotifierJob>();
        _factory.Time.Advance(TimeSpan.FromDays(1));
        await _factory.RunJobAsync<TrialEndingNotifierJob>();

        (await _factory.CountOutboxAsync(nameof(TrialEnding), tenant.TenantId.ToString())).ShouldBe(1);
    }

    [Fact]
    public async Task ChangePlan_DuringTrial_IsImmediate()
    {
        await EnsurePlanAsync("pro-x", 14_900, maxSeats: 10);
        var tenant = await Given.ActiveTenantAsync(_factory);

        var response = await ChangePlanAsync(tenant, "pro-x", "annual");

        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("effective").GetString().ShouldBe("immediate");
        var subscription = await GetSubscriptionAsync(tenant);
        subscription.GetProperty("status").GetString().ShouldBe("Trialing");
        subscription.GetProperty("plan").GetProperty("code").GetString().ShouldBe("pro-x");
        subscription.GetProperty("interval").GetString().ShouldBe("annual");
    }

    [Fact]
    public async Task Upgrade_IsImmediate_AndInvalidatesTheEntitlementsCache()
    {
        await EnsurePlanAsync("pro-x", 14_900, maxSeats: 10);
        var tenant = await ActiveSubscriptionAsync();
        (await EntitlementsAsync(tenant.TenantId))!.MaxSeats.ShouldBe(3);

        var response = await ChangePlanAsync(tenant, "pro-x", "monthly");

        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("effective").GetString().ShouldBe("immediate");
        (await EntitlementsAsync(tenant.TenantId))!.MaxSeats.ShouldBe(10);
    }

    [Fact]
    public async Task Downgrade_IsScheduled_ThenAppliedByThePeriodEndJob_AndDoubleRunIsNoOp()
    {
        await EnsurePlanAsync("pro-x", 14_900, maxSeats: 10);
        var tenant = await ActiveSubscriptionAsync("pro-x");

        var response = await ChangePlanAsync(tenant, "starter", "monthly");
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("effective").GetString().ShouldBe("period_end");

        var scheduled = await GetSubscriptionAsync(tenant);
        scheduled.GetProperty("plan").GetProperty("code").GetString().ShouldBe("pro-x");
        scheduled.GetProperty("pendingChange").GetProperty("planCode").GetString().ShouldBe("starter");
        (await EntitlementsAsync(tenant.TenantId))!.MaxSeats.ShouldBe(3, "a pending downgrade limits new seats to min(current, target)");

        var end = scheduled.GetProperty("currentPeriodEnd").GetDateTimeOffset();
        _factory.Time.SetUtcNow(end.AddMinutes(5));
        (await _factory.RunJobAsync<PeriodEndJob>()).ShouldBe(1);
        (await _factory.RunJobAsync<PeriodEndJob>()).ShouldBe(0);

        var applied = await GetSubscriptionAsync(tenant);
        applied.GetProperty("plan").GetProperty("code").GetString().ShouldBe("starter");
        applied.TryGetProperty("pendingChange", out var pending).ShouldBeTrue();
        pending.ValueKind.ShouldBe(JsonValueKind.Null);
        applied.GetProperty("currentPeriodStart").GetDateTimeOffset().ShouldBe(end);
    }

    [Fact]
    public async Task Downgrade_WithMoreSeatsThanTheTarget_Is409WithDetails()
    {
        await EnsurePlanAsync("pro-x", 14_900, maxSeats: 10);
        await EnsurePlanAsync("solo", 1_900, maxSeats: 1);
        var tenant = await ActiveSubscriptionAsync("pro-x");
        var admin = await AdminAsync(tenant);
        (await admin.PostAsJsonAsync("/v1/users/invitations", new { email = $"m-{Guid.NewGuid():N}@x.test", role = "Member" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await admin.PostAsJsonAsync("/v1/subscription/change-plan", new { planCode = "solo", interval = "monthly" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("seats-exceed-target-plan");
        problem.GetProperty("activeSeats").GetInt32().ShouldBe(2);
        problem.GetProperty("targetMaxSeats").GetInt32().ShouldBe(1);
        (await GetSubscriptionAsync(tenant)).GetProperty("pendingChange").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task CancelPendingChange_RemovesTheScheduledDowngrade()
    {
        await EnsurePlanAsync("pro-x", 14_900, maxSeats: 10);
        var tenant = await ActiveSubscriptionAsync("pro-x");
        await ChangePlanAsync(tenant, "starter", "monthly");

        var response = await (await AdminAsync(tenant)).DeleteAsync(new Uri("/v1/subscription/pending-change", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("pendingChange").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("starter", "monthly", HttpStatusCode.UnprocessableEntity, "no-change")]
    [InlineData("does-not-exist", "monthly", HttpStatusCode.UnprocessableEntity, "plan-unavailable")]
    public async Task ChangePlan_InvalidTarget_ReturnsProblem(string plan, string interval, HttpStatusCode status, string code)
    {
        var tenant = await ActiveSubscriptionAsync();

        var response = await (await AdminAsync(tenant)).PostAsJsonAsync("/v1/subscription/change-plan", new { planCode = plan, interval }, Ct);

        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Cancel_IsIdempotent_Resume_Reverts_AndPeriodEndCancels()
    {
        var tenant = await ActiveSubscriptionAsync();
        var admin = await AdminAsync(tenant);

        (await admin.PostAsync(new Uri("/v1/subscription/cancel", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var canceled = await admin.PostAsync(new Uri("/v1/subscription/cancel", UriKind.Relative), null, Ct);
        canceled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await canceled.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("cancelAtPeriodEnd").GetBoolean().ShouldBeTrue();

        var resumed = await admin.PostAsync(new Uri("/v1/subscription/resume", UriKind.Relative), null, Ct);
        (await resumed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("cancelAtPeriodEnd").GetBoolean().ShouldBeFalse();

        await admin.PostAsync(new Uri("/v1/subscription/cancel", UriKind.Relative), null, Ct);
        _factory.Time.SetUtcNow((await GetSubscriptionAsync(tenant)).GetProperty("currentPeriodEnd").GetDateTimeOffset());
        await _factory.RunJobAsync<PeriodEndJob>();

        (await GetSubscriptionAsync(tenant)).GetProperty("status").GetString().ShouldBe("Canceled");
        (await EntitlementsAsync(tenant.TenantId))!.ProductAccessAllowed.ShouldBeFalse();
        (await _factory.CountOutboxAsync(nameof(SubscriptionCanceled), tenant.TenantId.ToString())).ShouldBe(1);
    }

    [Fact]
    public async Task Reactivate_FromExpired_RequiresAPaymentMethod_ThenStartsAFullPeriod()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        _factory.Time.Advance(TimeSpan.FromDays(14));
        await _factory.RunJobAsync<TrialExpiryJob>();
        var admin = await AdminAsync(tenant);

        (await admin.PostAsync(new Uri("/v1/subscription/reactivate", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);

        FakePaymentsApi.WithPaymentMethod[tenant.TenantId] = true;
        _factory.Time.Advance(TimeSpan.FromDays(3));
        var response = await admin.PostAsync(new Uri("/v1/subscription/reactivate", UriKind.Relative), null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("status").GetString().ShouldBe("Active");
        body.GetProperty("currentPeriodStart").GetDateTimeOffset().ShouldBe(_factory.Time.GetUtcNow());
        (await EntitlementsAsync(tenant.TenantId))!.ProductAccessAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task PaymentEvents_DrivePastDueSuspendedAndBackToActive_AndSuspensionExpires()
    {
        var tenant = await ActiveSubscriptionAsync();
        var invoiceId = Guid.NewGuid();

        await _factory.HandleAsync(new PaymentFailed(tenant.TenantId, invoiceId, 1, "card_declined", null, _factory.Time.GetUtcNow()));
        (await GetSubscriptionAsync(tenant)).GetProperty("status").GetString().ShouldBe("PastDue");

        await _factory.HandleAsync(new PaymentRetriesExhausted(tenant.TenantId, invoiceId, _factory.Time.GetUtcNow()));
        await _factory.HandleAsync(new PaymentRetriesExhausted(tenant.TenantId, invoiceId, _factory.Time.GetUtcNow()));
        (await GetSubscriptionAsync(tenant)).GetProperty("status").GetString().ShouldBe("Suspended");
        (await EntitlementsAsync(tenant.TenantId))!.ProductAccessAllowed.ShouldBeFalse();

        await _factory.HandleAsync(new InvoicePaid(invoiceId, tenant.TenantId, Guid.Empty, 4_900, _factory.Time.GetUtcNow()));
        (await GetSubscriptionAsync(tenant)).GetProperty("status").GetString().ShouldBe("Active");

        await _factory.HandleAsync(new PaymentFailed(tenant.TenantId, invoiceId, 1, "card_declined", null, _factory.Time.GetUtcNow()));
        await _factory.HandleAsync(new PaymentRetriesExhausted(tenant.TenantId, invoiceId, _factory.Time.GetUtcNow()));
        _factory.Time.Advance(TimeSpan.FromDays(30));
        (await _factory.RunJobAsync<SuspensionExpiryJob>()).ShouldBe(1);
        (await _factory.RunJobAsync<SuspensionExpiryJob>()).ShouldBe(0);
        (await GetSubscriptionAsync(tenant)).GetProperty("status").GetString().ShouldBe("Canceled");
    }

    [Fact]
    public async Task ConcurrentUpdates_TheSecondGets409()
    {
        var tenant = await ActiveSubscriptionAsync();
        await using var first = _factory.Services.CreateAsyncScope();
        await using var second = _factory.Services.CreateAsyncScope();
        var storeA = first.ServiceProvider.GetRequiredService<SubscriptionStore>();
        var storeB = second.ServiceProvider.GetRequiredService<SubscriptionStore>();
        var a = (await storeA.FindByTenantAsync(tenant.TenantId, Ct))!;
        var b = (await storeB.FindByTenantAsync(tenant.TenantId, Ct))!;

        a.Cancel(_factory.Time.GetUtcNow());
        b.MarkPastDue(_factory.Time.GetUtcNow());

        (await storeA.SaveAsync(a, Ct)).ShouldBeNull();
        (await storeB.SaveAsync(b, Ct))!.Code.ShouldBe("concurrency-conflict");
    }

    [Fact]
    public async Task Authorization_MemberReadsOnly_TenantsAreIsolated_AdminListIsPlatformOnly()
    {
        var a = await Given.ActiveTenantAsync(_factory);
        var b = await Given.ActiveTenantAsync(_factory);
        var member = _factory.CreateClient().WithBearer(TestTokens.Issue(Subscribely.Infrastructure.Tenancy.Roles.Member, a.TenantId));

        (await member.GetAsync(new Uri("/v1/subscription", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await member.PostAsync(new Uri("/v1/subscription/cancel", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await (await AdminAsync(b)).PostAsync(new Uri("/v1/subscription/cancel", UriKind.Relative), null, Ct);
        (await GetSubscriptionAsync(a)).GetProperty("cancelAtPeriodEnd").GetBoolean().ShouldBeFalse();

        (await (await AdminAsync(a)).GetAsync(new Uri("/admin/subscriptions", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var all = await _factory.CreateClient().AsPlatformAdmin().GetFromJsonAsync<List<JsonElement>>("/admin/subscriptions?status=Trialing", Ct);
        all!.Select(s => s.GetProperty("tenantId").GetGuid()).ShouldContain(a.TenantId);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<TenantScenario> ActiveSubscriptionAsync(string plan = "starter")
    {
        await Given.EnsurePlanAsync(_factory, "starter");
        var tenant = await Given.ActiveTenantAsync(_factory, plan);
        FakePaymentsApi.WithPaymentMethod[tenant.TenantId] = true;
        _factory.Time.Advance(TimeSpan.FromDays(14));
        await _factory.RunJobAsync<TrialExpiryJob>();
        (await GetSubscriptionAsync(tenant)).GetProperty("status").GetString().ShouldBe("Active");
        return tenant;
    }

    private async Task EnsurePlanAsync(string code, long monthly, int maxSeats)
    {
        var response = await _factory.CreateClient().AsPlatformAdmin().PostAsJsonAsync("/admin/plans", new
        {
            code,
            name = code,
            monthlyPriceCents = monthly,
            annualPriceCents = monthly * 10,
            requestQuotaMonthly = 1_000,
            maxSeats,
        }, Ct);
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    /// <summary>Token de longa duração: o relógio falso avança dias e o access token do login vive 15 min.</summary>
    private Task<HttpClient> AdminAsync(TenantScenario tenant) =>
        Task.FromResult(_factory.CreateClient().WithBearer(TestTokens.Issue(Subscribely.Infrastructure.Tenancy.Roles.TenantAdmin, tenant.TenantId)));

    private async Task<JsonElement> GetSubscriptionAsync(TenantScenario tenant) =>
        await (await AdminAsync(tenant)).GetFromJsonAsync<JsonElement>("/v1/subscription", Ct);

    private async Task<HttpResponseMessage> ChangePlanAsync(TenantScenario tenant, string planCode, string interval)
    {
        var response = await (await AdminAsync(tenant)).PostAsJsonAsync("/v1/subscription/change-plan", new { planCode, interval }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return response;
    }

    private async Task<Entitlements?> EntitlementsAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBillingApi>().GetEntitlementsAsync(tenantId, Ct);
    }
}
