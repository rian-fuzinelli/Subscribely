using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.IntegrationTests.Infrastructure.Scenario;
using Subscribely.Modules.Payments.Domain;
using Subscribely.Modules.Payments.Features.Charges;
using Subscribely.Modules.Payments.Features.Webhooks;
using Subscribely.Modules.Payments.Gateway;
using Subscribely.Modules.Payments.Persistence;

namespace Subscribely.IntegrationTests.Payments;

/// <summary>Caminhos de falha do módulo Payments: webhook inválido, charge ainda não gravada, fatura anulada.</summary>
public sealed class PaymentEdgeCaseTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Webhook_UnknownProvider_Is404_AndMalformedBody_Is400()
    {
        const string body = "not json";

        (await PostAsync("/webhooks/payments/stripe", body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync("/webhooks/payments/fake", body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync("/webhooks/payments/fake", "{}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Webhook_ForAChargeNotSavedYet_IsKeptForRetry()
    {
        var payload = new WebhookPayload("evt_early", WebhookPayload.Succeeded, _factory.Time.GetUtcNow(), new WebhookChargeData("ch_unknown", Guid.NewGuid(), 100, null));

        (await PostAsync("/webhooks/payments/fake", JsonSerializer.Serialize(payload, WebhookPayload.Json))).StatusCode.ShouldBe(HttpStatusCode.OK);

        _factory.Jobs.Failures.ShouldHaveSingleItem().ShouldBeOfType<ChargeNotFoundYetException>();
        (await _factory.WithDbAsync<PaymentsDbContext, PaymentEventStatus>(db => db.PaymentEvents.Select(e => e.Status).SingleAsync(Ct)))
            .ShouldBe(PaymentEventStatus.Received);
    }

    [Fact]
    public async Task UnknownEventType_IsIgnored_AndSuccessForAVoidedInvoice_IsKeptForReconciliation()
    {
        var tenant = await Journey.SignsUpAsync(_factory);
        await tenant.RegistersCardAsync("pm_card_ok");
        _factory.Time.Advance(TimeSpan.FromDays(14));
        await _factory.RunJobAsync<Modules.Billing.Features.Subscriptions.Jobs.TrialExpiryJob>();
        await _factory.DrainOutboxAsync(); // charge criada; webhook do gateway ainda não entregue
        var charge = await _factory.WithDbAsync<PaymentsDbContext, Charge>(db => db.Charges.SingleAsync(Ct));

        await SendAsync("charge.refunded", charge);
        (await LastStatusAsync()).ShouldBe(PaymentEventStatus.Ignored);

        (await _factory.CreateClient().AsPlatformAdmin().PostAsJsonAsync($"/admin/invoices/{charge.InvoiceId}/void", new { reason = "teste de reconciliação" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await SendAsync(WebhookPayload.Succeeded, charge);

        (await LastStatusAsync()).ShouldBe(PaymentEventStatus.Processed);
        (await tenant.InvoiceAsync(charge.InvoiceId)).GetProperty("status").GetString().ShouldBe("Void");
    }

    [Fact]
    public async Task Dunning_ForAnInvoiceNoLongerOpen_IsResolvedWithoutCharging()
    {
        var tenant = await Journey.SignsUpAsync(_factory);
        await tenant.RegistersCardAsync("pm_card_declined");
        await _factory.AdvanceAsync(TimeSpan.FromDays(14));
        var invoiceId = (await tenant.InvoicesAsync()).Single().GetProperty("id").GetGuid();
        await _factory.CreateClient().AsPlatformAdmin().PostAsJsonAsync($"/admin/invoices/{invoiceId}/void", new { reason = "perdoada" }, Ct);

        _factory.Time.Advance(TimeSpan.FromDays(1));
        (await _factory.RunJobAsync<DunningJob>()).ShouldBe(0);

        var dunning = await _factory.WithDbAsync<PaymentsDbContext, Dunning>(db => db.Dunnings.SingleAsync(Ct));
        dunning.ResolvedAt.ShouldNotBeNull();
        (await _factory.WithDbAsync<PaymentsDbContext, int>(db => db.Charges.CountAsync(Ct))).ShouldBe(1);
    }

    [Fact]
    public async Task PayNow_WithoutAPaymentMethod_Is402()
    {
        var tenant = await Journey.SignsUpAsync(_factory);
        await tenant.RegistersCardAsync("pm_card_declined");
        await _factory.AdvanceAsync(TimeSpan.FromDays(14));
        var invoiceId = (await tenant.InvoicesAsync()).Single().GetProperty("id").GetGuid();
        await _factory.WithDbAsync<PaymentsDbContext, int>(db => db.PaymentMethods.ExecuteDeleteAsync(Ct));

        var response = await tenant.PostsAsync($"/v1/invoices/{invoiceId}/pay");

        response.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("payment-method-required");
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private Task<HttpResponseMessage> PostAsync(string path, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add(WebhookSignature.HeaderName, WebhookSignature.Sign(SubscribelyApiFactory.WebhookSecret, body, _factory.Time.GetUtcNow()));
        return _factory.CreateClient().SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> SendAsync(string type, Charge charge) =>
        PostAsync("/webhooks/payments/fake", JsonSerializer.Serialize(
            new WebhookPayload("evt_" + Guid.NewGuid().ToString("N"), type, _factory.Time.GetUtcNow(), new WebhookChargeData(charge.GatewayChargeId!, charge.InvoiceId, charge.AmountCents, null)),
            WebhookPayload.Json));

    private Task<PaymentEventStatus> LastStatusAsync() =>
        _factory.WithDbAsync<PaymentsDbContext, PaymentEventStatus>(db =>
            db.PaymentEvents.OrderByDescending(e => e.ReceivedAt).ThenByDescending(e => e.Id).Select(e => e.Status).FirstAsync(Ct));
}

/// <summary>Gateway inacessível e cobrança desligada (<c>Payments:Provider = None</c>).</summary>
public sealed class PaymentGatewayFailureTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _unavailable = new(containers, s => s["Payments:GatewayBaseUrl"] = "http://localhost/no-gateway/");
    private readonly SubscribelyApiFactory _disabled = new(containers, s => s["Payments:Provider"] = "None");

    [Fact]
    public async Task GatewayDown_FailsTheChargeAsUnavailable_AndStartsDunning()
    {
        var tenant = await Journey.SignsUpAsync(_unavailable);
        await tenant.RegistersCardAsync("pm_card_ok");

        await _unavailable.AdvanceAsync(TimeSpan.FromDays(14));

        var charge = await _unavailable.WithDbAsync<PaymentsDbContext, Charge>(db => db.Charges.SingleAsync(TestContext.Current.CancellationToken));
        charge.Status.ShouldBe(ChargeStatus.Failed);
        charge.FailureCode.ShouldBe(Charge.GatewayUnavailable);
        (await tenant.StatusAsync()).ShouldBe("PastDue");
    }

    [Fact]
    public async Task ProviderNone_IssuesTheInvoiceWithoutCharging()
    {
        var tenant = await Journey.SignsUpAsync(_disabled);
        await tenant.RegistersCardAsync("pm_card_ok");

        await _disabled.AdvanceAsync(TimeSpan.FromDays(14));

        (await tenant.InvoicesAsync()).Single().GetProperty("status").GetString().ShouldBe("Open");
        (await _disabled.WithDbAsync<PaymentsDbContext, int>(db => db.Charges.CountAsync(TestContext.Current.CancellationToken))).ShouldBe(0);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _unavailable.DisposeAsync();
        await _disabled.DisposeAsync();
    }
}
