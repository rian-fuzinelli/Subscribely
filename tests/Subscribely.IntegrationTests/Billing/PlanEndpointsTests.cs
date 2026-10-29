using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.Modules.Billing.Contracts;

namespace Subscribely.IntegrationTests.Billing;

public sealed class PlanEndpointsTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_AsPlatformAdmin_ReturnsCreatedAndIsListedPublicly()
    {
        var code = NewCode();
        var admin = _factory.CreateClient().AsPlatformAdmin();

        var response = await admin.PostAsJsonAsync("/admin/plans", NewPlan(code), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var plans = await _factory.CreateClient().GetFromJsonAsync<List<PlanDto>>("/v1/plans", Ct);
        plans.ShouldNotBeNull().ShouldContain(p => p.Code == code && p.Version == 1 && p.MaxSeats == 5);
    }

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/admin/plans", NewPlan(NewCode()), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_AsTenantAdmin_Returns403()
    {
        var client = _factory.CreateClient().WithBearer(TestTokens.Issue(Roles.TenantAdmin, Guid.NewGuid()));

        var response = await client.PostAsJsonAsync("/admin/plans", NewPlan(NewCode()), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_DuplicateCode_Returns409()
    {
        var code = NewCode();
        var admin = _factory.CreateClient().AsPlatformAdmin();
        await admin.PostAsJsonAsync("/admin/plans", NewPlan(code), Ct);

        var response = await admin.PostAsJsonAsync("/admin/plans", NewPlan(code), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_AnnualAboveTwelveMonths_Returns422()
    {
        var response = await _factory.CreateClient().AsPlatformAdmin()
            .PostAsJsonAsync("/admin/plans", NewPlan(NewCode()) with { AnnualPriceCents = 1_000 * 12 + 1 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_InvalidCode_Returns400()
    {
        var response = await _factory.CreateClient().AsPlatformAdmin()
            .PostAsJsonAsync("/admin/plans", NewPlan("Invalid Code!"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_CreatesNewVersion()
    {
        var code = NewCode();
        var admin = _factory.CreateClient().AsPlatformAdmin();
        await admin.PostAsJsonAsync("/admin/plans", NewPlan(code), Ct);

        var response = await admin.PutAsJsonAsync($"/admin/plans/{code}", NewPlan(code) with { MaxSeats = 9 }, Ct);
        var plan = await response.Content.ReadFromJsonAsync<PlanDto>(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        plan!.Version.ShouldBe(2);
        plan.MaxSeats.ShouldBe(9);
    }

    [Fact]
    public async Task Archive_HidesPlanFromNewSubscriptions()
    {
        var code = NewCode();
        var admin = _factory.CreateClient().AsPlatformAdmin();
        await admin.PostAsJsonAsync("/admin/plans", NewPlan(code), Ct);

        (await admin.PostAsync(new Uri($"/admin/plans/{code}/archive", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var plans = await _factory.CreateClient().GetFromJsonAsync<List<PlanDto>>("/v1/plans", Ct);
        plans.ShouldNotBeNull().ShouldNotContain(p => p.Code == code);
        await using var scope = _factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IBillingApi>().GetActivePlanVersionAsync(code, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Update_UnknownPlan_Returns404()
    {
        var response = await _factory.CreateClient().AsPlatformAdmin()
            .PutAsJsonAsync("/admin/plans/does-not-exist", NewPlan("does-not-exist"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static string NewCode() => "p-" + Guid.NewGuid().ToString("N")[..12];

    private static PlanRequest NewPlan(string code) => new(code, "Test plan", 1_000, 10_000, 500, 5);

    private sealed record PlanRequest(string Code, string Name, long MonthlyPriceCents, long AnnualPriceCents, int RequestQuotaMonthly, int MaxSeats);

    private sealed record PlanDto(string Code, string Name, string Status, int Version, long MonthlyPriceCents, long AnnualPriceCents, int RequestQuotaMonthly, int MaxSeats);
}
