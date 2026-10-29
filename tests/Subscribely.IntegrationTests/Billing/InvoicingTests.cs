using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Domain.Invoices;
using Subscribely.Modules.Billing.Domain.Subscriptions;
using Subscribely.Modules.Billing.Features.Subscriptions.Jobs;
using Subscribely.Modules.Billing.Persistence;
using Subscribely.Modules.Payments.Contracts;

namespace Subscribely.IntegrationTests.Billing;

public sealed class InvoicingTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers, configureServices: services =>
    {
        services.RemoveAll<IPaymentsApi>();
        services.AddSingleton<IPaymentsApi, FakePaymentsApi>();
    });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TrialConversion_IssuesAnOpenInvoice_ForTheFirstPeriod()
    {
        var tenant = await ActiveSubscriptionAsync();

        var invoice = (await ListAsync(tenant)).ShouldHaveSingleItem();

        invoice.GetProperty("reason").GetString().ShouldBe("TrialConversion");
        invoice.GetProperty("status").GetString().ShouldBe("Open");
        invoice.GetProperty("totalCents").GetInt64().ShouldBe(4_900);
        invoice.GetProperty("currency").GetString().ShouldBe("BRL");
        invoice.GetProperty("number").GetString()!.ShouldMatch(@"^SUB-2026-\d{6}$");
        (await _factory.CountOutboxAsync(nameof(InvoiceIssued), invoice.GetProperty("id").GetGuid().ToString())).ShouldBe(1);
    }

    [Fact]
    public async Task Upgrade_IssuesAProratedInvoice_WithCreditAndChargeLines()
    {
        await EnsurePlanAsync("pro-x", 14_900);
        var tenant = await ActiveSubscriptionAsync();
        var subscription = await (await AdminAsync(tenant)).GetFromJsonAsync<JsonElement>("/v1/subscription", Ct);
        var start = subscription.GetProperty("currentPeriodStart").GetDateTimeOffset();
        var end = subscription.GetProperty("currentPeriodEnd").GetDateTimeOffset();
        _factory.Time.SetUtcNow(start + ((end - start) / 2));

        var response = await (await AdminAsync(tenant)).PostAsJsonAsync("/v1/subscription/change-plan", new { planCode = "pro-x", interval = "monthly" }, Ct);

        var invoiceId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("invoiceId").GetGuid();
        var invoice = await (await AdminAsync(tenant)).GetFromJsonAsync<JsonElement>($"/v1/invoices/{invoiceId}", Ct);
        invoice.GetProperty("reason").GetString().ShouldBe("Upgrade");
        var amounts = invoice.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("amountCents").GetInt64()).ToList();
        amounts.ShouldBe([-2_450, 7_450]);
        invoice.GetProperty("totalCents").GetInt64().ShouldBe(5_000);
    }

    [Fact]
    public async Task Renewal_IssuesOneInvoicePerPeriod_AndTheUniqueIndexRejectsDuplicates()
    {
        var tenant = await ActiveSubscriptionAsync();
        var end = (await (await AdminAsync(tenant)).GetFromJsonAsync<JsonElement>("/v1/subscription", Ct)).GetProperty("currentPeriodEnd").GetDateTimeOffset();
        _factory.Time.SetUtcNow(end.AddMinutes(1));

        await _factory.RunJobAsync<PeriodEndJob>();
        await _factory.RunJobAsync<PeriodEndJob>();

        var renewal = (await ListAsync(tenant)).Where(i => i.GetProperty("reason").GetString() == "Renewal").ShouldHaveSingleItem();
        renewal.GetProperty("periodStart").GetDateTimeOffset().ShouldBe(end);

        var duplicated = await Should.ThrowAsync<DbUpdateException>(() => _factory.WithDbAsync<BillingDbContext, int>(async db =>
        {
            var subscription = await db.Subscriptions.FirstAsync(s => s.TenantId == tenant.TenantId, Ct);
            var (period, lines) = InvoiceFactory.FullPeriod(subscription, await db.PlanVersions.FirstAsync(v => v.Id == subscription.PlanVersionId, Ct));
            db.Invoices.Add(Invoice.Issue("SUB-2026-999999", subscription, InvoiceReason.Renewal, period, lines, _factory.Time.GetUtcNow()));
            return await db.SaveChangesAsync(Ct);
        }));
        duplicated.InnerException.ShouldBeOfType<Npgsql.PostgresException>().SqlState.ShouldBe("23505");
    }

    [Fact]
    public async Task List_PaginatesByCursor_MostRecentFirst()
    {
        var tenant = await ActiveSubscriptionAsync();
        for (var i = 0; i < 3; i++)
        {
            var end = (await (await AdminAsync(tenant)).GetFromJsonAsync<JsonElement>("/v1/subscription", Ct)).GetProperty("currentPeriodEnd").GetDateTimeOffset();
            _factory.Time.SetUtcNow(end);
            await _factory.RunJobAsync<PeriodEndJob>();
        }

        var admin = await AdminAsync(tenant);
        var first = await admin.GetFromJsonAsync<JsonElement>("/v1/invoices?limit=3", Ct);
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await admin.GetFromJsonAsync<JsonElement>($"/v1/invoices?limit=3&cursor={cursor}", Ct);

        var numbers = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("number").GetString()).ToList();
        numbers.Count.ShouldBe(4);
        numbers.ShouldBe(numbers.OrderDescending().ToList());
        second.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
        (await admin.GetAsync(new Uri("/v1/invoices?cursor=@@@", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Invoices_AreForTheTenantAdminOnly_AndIsolatedBetweenTenants()
    {
        var a = await ActiveSubscriptionAsync();
        var b = await Given.ActiveTenantAsync(_factory);
        var invoiceA = (await ListAsync(a)).Single().GetProperty("id").GetGuid();

        var member = _factory.CreateClient().WithBearer(TestTokens.Issue(Roles.Member, a.TenantId));
        (await member.GetAsync(new Uri("/v1/invoices", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await (await AdminAsync(b)).GetAsync(new Uri($"/v1/invoices/{invoiceA}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(b)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Void_IsPlatformAdminOnly_RequiresAReason_AndPaidInvoicesCannotBeVoided()
    {
        var tenant = await ActiveSubscriptionAsync();
        var invoiceId = (await ListAsync(tenant)).Single().GetProperty("id").GetGuid();
        var platform = _factory.CreateClient().AsPlatformAdmin();
        var url = new Uri($"/admin/invoices/{invoiceId}/void", UriKind.Relative);

        (await (await AdminAsync(tenant)).PostAsJsonAsync(url, new { reason = "x-tenant" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await platform.PostAsJsonAsync(url, new { reason = "" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var voided = await platform.PostAsJsonAsync(url, new { reason = "emitida por engano" }, Ct);
        voided.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await voided.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("status").GetString().ShouldBe("Void");
        body.GetProperty("voidReason").GetString().ShouldBe("emitida por engano");
        (await _factory.CountOutboxAsync(nameof(InvoiceVoided), invoiceId.ToString())).ShouldBe(1);

        await using var scope = _factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IBillingApi>().MarkInvoicePaidAsync(invoiceId, _factory.Time.GetUtcNow(), Ct))
            .ShouldBe(MarkInvoicePaidOutcome.Void);
    }

    [Fact]
    public async Task MarkInvoicePaid_IsIdempotent_PublishesOneEvent_AndPaidInvoiceCannotBeVoided()
    {
        var tenant = await ActiveSubscriptionAsync();
        var invoiceId = (await ListAsync(tenant)).Single().GetProperty("id").GetGuid();

        (await MarkPaidAsync(invoiceId)).ShouldBe(MarkInvoicePaidOutcome.Paid);
        (await MarkPaidAsync(invoiceId)).ShouldBe(MarkInvoicePaidOutcome.AlreadyPaid);
        (await MarkPaidAsync(Guid.NewGuid())).ShouldBe(MarkInvoicePaidOutcome.NotFound);

        (await _factory.CountOutboxAsync(nameof(InvoicePaid), invoiceId.ToString())).ShouldBe(1);
        var voided = await _factory.CreateClient().AsPlatformAdmin().PostAsJsonAsync($"/admin/invoices/{invoiceId}/void", new { reason = "tarde" }, Ct);
        voided.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SuspensionExpiry_VoidsTheOpenInvoice_WithTheSystemReason()
    {
        var tenant = await ActiveSubscriptionAsync();
        var invoiceId = (await ListAsync(tenant)).Single().GetProperty("id").GetGuid();
        await _factory.HandleAsync(new PaymentFailed(tenant.TenantId, invoiceId, 1, "card_declined", null, _factory.Time.GetUtcNow()));
        await _factory.HandleAsync(new PaymentRetriesExhausted(tenant.TenantId, invoiceId, _factory.Time.GetUtcNow()));

        _factory.Time.Advance(TimeSpan.FromDays(30));
        await _factory.RunJobAsync<SuspensionExpiryJob>();

        var invoice = await (await AdminAsync(tenant)).GetFromJsonAsync<JsonElement>($"/v1/invoices/{invoiceId}", Ct);
        invoice.GetProperty("status").GetString().ShouldBe("Void");
        invoice.GetProperty("voidReason").GetString().ShouldBe(SuspensionExpiryJob.VoidReason);
        var status = await _factory.WithDbAsync<BillingDbContext, SubscriptionStatus>(db =>
            db.Subscriptions.Where(s => s.TenantId == tenant.TenantId).Select(s => s.Status).FirstAsync(Ct));
        status.ShouldBe(SubscriptionStatus.Canceled);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<TenantScenario> ActiveSubscriptionAsync()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        FakePaymentsApi.WithPaymentMethod[tenant.TenantId] = true;
        _factory.Time.Advance(TimeSpan.FromDays(14));
        await _factory.RunJobAsync<TrialExpiryJob>();
        return tenant;
    }

    private async Task EnsurePlanAsync(string code, long monthly)
    {
        var response = await _factory.CreateClient().AsPlatformAdmin().PostAsJsonAsync("/admin/plans", new
        {
            code,
            name = code,
            monthlyPriceCents = monthly,
            annualPriceCents = monthly * 10,
            requestQuotaMonthly = 1_000,
            maxSeats = 10,
        }, Ct);
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    private Task<HttpClient> AdminAsync(TenantScenario tenant) =>
        Task.FromResult(_factory.CreateClient().WithBearer(TestTokens.Issue(Roles.TenantAdmin, tenant.TenantId)));

    private async Task<List<JsonElement>> ListAsync(TenantScenario tenant) =>
        (await (await AdminAsync(tenant)).GetFromJsonAsync<JsonElement>("/v1/invoices", Ct)).GetProperty("items").EnumerateArray().ToList();

    private async Task<MarkInvoicePaidOutcome> MarkPaidAsync(Guid invoiceId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBillingApi>().MarkInvoicePaidAsync(invoiceId, _factory.Time.GetUtcNow(), Ct);
    }
}
