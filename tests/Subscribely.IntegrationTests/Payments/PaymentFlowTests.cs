using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.Modules.Billing.Domain.Subscriptions;
using Subscribely.Modules.Billing.Features.Subscriptions.Jobs;
using Subscribely.Modules.Billing.Persistence;
using Subscribely.Modules.Payments.Domain;
using Subscribely.Modules.Payments.Features.Charges;
using Subscribely.Modules.Payments.Features.Webhooks;
using Subscribely.Modules.Payments.Gateway;
using Subscribely.Modules.Payments.Persistence;

namespace Subscribely.IntegrationTests.Payments;

public sealed class PaymentFlowTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CardOk_TrialConversion_IsChargedAndPaid()
    {
        var tenant = await TrialWithCardAsync("pm_card_ok");

        await ConvertTrialAsync();

        var invoice = await SingleInvoiceAsync(tenant);
        invoice.GetProperty("status").GetString().ShouldBe("Paid");
        (await StatusAsync(tenant)).ShouldBe(SubscriptionStatus.Active);
        var charge = await _factory.WithDbAsync<PaymentsDbContext, Charge>(db => db.Charges.SingleAsync(c => c.TenantId == tenant.TenantId, Ct));
        charge.Status.ShouldBe(ChargeStatus.Succeeded);
        charge.IdempotencyKey.ShouldBe($"{invoice.GetProperty("id").GetGuid()}:1");
        _factory.Jobs.Failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithoutPaymentMethod_NoChargeIsCreated()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);

        await ConvertTrialAsync();

        (await StatusAsync(tenant)).ShouldBe(SubscriptionStatus.Expired);
        (await _factory.WithDbAsync<PaymentsDbContext, int>(db => db.Charges.CountAsync(c => c.TenantId == tenant.TenantId, Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task CardDeclined_DunningGoesD1D3D7ToSuspended_ThenANewCardReactivates()
    {
        var tenant = await TrialWithCardAsync("pm_card_declined");
        await ConvertTrialAsync();
        (await StatusAsync(tenant)).ShouldBe(SubscriptionStatus.PastDue);
        var issuedAt = (await SingleInvoiceAsync(tenant)).GetProperty("issuedAt").GetDateTimeOffset();

        foreach (var day in new[] { 1, 3 })
        {
            _factory.Time.SetUtcNow(issuedAt.AddDays(day));
            (await _factory.RunJobAsync<DunningJob>()).ShouldBe(1);
            (await _factory.RunJobAsync<DunningJob>()).ShouldBe(0, "the retry is paused while the charge is pending");
            await DeliverWebhooksAsync();
            (await StatusAsync(tenant)).ShouldBe(SubscriptionStatus.PastDue);
        }

        _factory.Time.SetUtcNow(issuedAt.AddDays(7));
        await _factory.RunJobAsync<DunningJob>();
        await DeliverWebhooksAsync();
        (await StatusAsync(tenant)).ShouldBe(SubscriptionStatus.Suspended);
        (await _factory.WithDbAsync<PaymentsDbContext, int>(db => db.Charges.CountAsync(c => c.TenantId == tenant.TenantId, Ct))).ShouldBe(4);

        var updated = await Admin(tenant).PutAsJsonAsync("/v1/billing/payment-method", new { token = "pm_card_ok" }, Ct);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("last4").GetString().ShouldBe("4242");
        await DeliverWebhooksAsync();

        (await StatusAsync(tenant)).ShouldBe(SubscriptionStatus.Active);
        (await SingleInvoiceAsync(tenant)).GetProperty("status").GetString().ShouldBe("Paid");
    }

    [Fact]
    public async Task FailThenOk_RecoversOnTheFirstRetry()
    {
        var tenant = await TrialWithCardAsync("pm_card_fail_then_ok");
        await ConvertTrialAsync();
        (await StatusAsync(tenant)).ShouldBe(SubscriptionStatus.PastDue);

        _factory.Time.Advance(TimeSpan.FromDays(1));
        await _factory.RunJobAsync<DunningJob>();
        await DeliverWebhooksAsync();

        (await StatusAsync(tenant)).ShouldBe(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task ReplayedWebhook_Returns200_WithoutANewEffect()
    {
        var tenant = await TrialWithCardAsync("pm_card_ok");
        await ConvertTrialAsync();
        var received = await _factory.WithDbAsync<PaymentsDbContext, PaymentEvent>(db => db.PaymentEvents.SingleAsync(Ct));

        var replay = await _factory.CreateClient().PostAsync(new Uri($"/dev/fake-gateway/events/{received.ProviderEventId}/replay", UriKind.Relative), null, Ct);
        await DeliverWebhooksAsync();

        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _factory.WithDbAsync<PaymentsDbContext, int>(db => db.PaymentEvents.CountAsync(Ct))).ShouldBe(1);
        (await _factory.CountOutboxAsync("InvoicePaid", tenant.TenantId.ToString())).ShouldBe(1);
    }

    [Fact]
    public async Task Webhook_WithBadSignature_Is401WithoutDetails()
    {
        var body = """{"id":"evt_x","type":"charge.succeeded","createdAt":"2026-01-01T00:00:00Z","data":{"chargeId":"ch_x","invoiceId":"00000000-0000-0000-0000-000000000000","amountCents":1}}""";
        var now = _factory.Time.GetUtcNow();

        (await PostWebhookAsync(body, "t=1,v1=00")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PostWebhookAsync(body, WebhookSignature.Sign(SubscribelyApiFactory.WebhookSecret, body, now.AddMinutes(-6)))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var tampered = await PostWebhookAsync(body.Replace("\"amountCents\":1", "\"amountCents\":2", StringComparison.Ordinal), WebhookSignature.Sign(SubscribelyApiFactory.WebhookSecret, body, now));
        tampered.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await tampered.Content.ReadAsStringAsync(Ct)).ShouldNotContain("signature", Case.Insensitive);
        (await _factory.WithDbAsync<PaymentsDbContext, int>(db => db.PaymentEvents.CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task OutOfOrderFailure_AfterSuccess_IsIgnored_AndAmountMismatch_IsRejected()
    {
        var paid = await TrialWithCardAsync("pm_card_ok");
        await ConvertTrialAsync();
        var charge = await _factory.WithDbAsync<PaymentsDbContext, Charge>(db => db.Charges.SingleAsync(c => c.TenantId == paid.TenantId, Ct));

        (await SendSignedAsync(WebhookPayload.Failed, charge.GatewayChargeId!, charge.InvoiceId, charge.AmountCents, "card_declined")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LastEventStatusAsync()).ShouldBe(PaymentEventStatus.Ignored);
        (await StatusAsync(paid)).ShouldBe(SubscriptionStatus.Active);

        // Charge pendente (webhook ainda não entregue) recebe um evento com valor divergente.
        var pending = await TrialWithCardAsync("pm_card_ok");
        _factory.Time.Advance(TimeSpan.FromDays(14));
        await _factory.RunJobAsync<TrialExpiryJob>();
        await _factory.DrainOutboxAsync();
        var open = await _factory.WithDbAsync<PaymentsDbContext, Charge>(db => db.Charges.SingleAsync(c => c.TenantId == pending.TenantId, Ct));

        await SendSignedAsync(WebhookPayload.Succeeded, open.GatewayChargeId!, open.InvoiceId, open.AmountCents + 1, null);

        (await LastEventStatusAsync()).ShouldBe(PaymentEventStatus.Rejected);
        (await SingleInvoiceAsync(pending)).GetProperty("status").GetString().ShouldBe("Open");
    }

    [Fact]
    public async Task PayNow_RetriesWithTheCurrentCard_409WhilePending_404AcrossTenants()
    {
        var tenant = await TrialWithCardAsync("pm_card_declined");
        await ConvertTrialAsync();
        var invoiceId = (await SingleInvoiceAsync(tenant)).GetProperty("id").GetGuid();
        var other = await Given.ActiveTenantAsync(_factory);

        (await Admin(other).PostAsync(new Uri($"/v1/invoices/{invoiceId}/pay", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var first = await Admin(tenant).PostAsync(new Uri($"/v1/invoices/{invoiceId}/pay", UriKind.Relative), null, Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("attempt").GetInt32().ShouldBe(2);
        (await Admin(tenant).PostAsync(new Uri($"/v1/invoices/{invoiceId}/pay", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await DeliverWebhooksAsync();
        var dunning = await _factory.WithDbAsync<PaymentsDbContext, Dunning>(db => db.Dunnings.SingleAsync(d => d.InvoiceId == invoiceId, Ct));
        dunning.Attempts.ShouldBe(1, "manual attempts do not consume the dunning schedule");
        dunning.NextAttemptAt.ShouldBe((await SingleInvoiceAsync(tenant)).GetProperty("issuedAt").GetDateTimeOffset().AddDays(1));
    }

    [Fact]
    public async Task PaymentMethod_IsTenantAdminOnly()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        var member = _factory.CreateClient().WithBearer(TestTokens.Issue(Roles.Member, tenant.TenantId));

        (await member.PutAsJsonAsync("/v1/billing/payment-method", new { token = "pm_card_ok" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin(tenant).PutAsJsonAsync("/v1/billing/payment-method", new { token = "4111111111111111" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin(tenant).GetAsync(new Uri("/v1/billing/payment-method", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Admin(TenantScenario tenant) =>
        _factory.CreateClient().WithBearer(TestTokens.Issue(Roles.TenantAdmin, tenant.TenantId));

    private async Task<TenantScenario> TrialWithCardAsync(string token)
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        (await Admin(tenant).PutAsJsonAsync("/v1/billing/payment-method", new { token }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return tenant;
    }

    /// <summary>Fim do trial → fatura → cobrança (outbox) → webhook (job agendado) → fatura paga (outbox).</summary>
    private async Task ConvertTrialAsync()
    {
        _factory.Time.Advance(TimeSpan.FromDays(14));
        await _factory.RunJobAsync<TrialExpiryJob>();
        await _factory.DrainOutboxAsync();
        await DeliverWebhooksAsync();
    }

    /// <summary>Entrega os webhooks agendados pelo gateway Fake (1–3 s no relógio real) e processa o outbox.</summary>
    private async Task DeliverWebhooksAsync()
    {
        await _factory.DrainOutboxAsync();
        await _factory.Jobs.RunDueAsync(DateTimeOffset.UtcNow.AddSeconds(10));
        await _factory.DrainOutboxAsync();
    }

    private async Task<SubscriptionStatus> StatusAsync(TenantScenario tenant) =>
        await _factory.WithDbAsync<BillingDbContext, SubscriptionStatus>(db =>
            db.Subscriptions.Where(s => s.TenantId == tenant.TenantId).Select(s => s.Status).FirstAsync(Ct));

    private async Task<JsonElement> SingleInvoiceAsync(TenantScenario tenant) =>
        (await Admin(tenant).GetFromJsonAsync<JsonElement>("/v1/invoices", Ct)).GetProperty("items").EnumerateArray().Single();

    private Task<HttpResponseMessage> PostWebhookAsync(string body, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhooks/payments/fake", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(WebhookSignature.HeaderName, signature);
        return _factory.CreateClient().SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> SendSignedAsync(string type, string chargeId, Guid invoiceId, long amountCents, string? failureCode)
    {
        var payload = new WebhookPayload("evt_" + Guid.NewGuid().ToString("N"), type, _factory.Time.GetUtcNow(), new WebhookChargeData(chargeId, invoiceId, amountCents, failureCode));
        var body = JsonSerializer.Serialize(payload, WebhookPayload.Json);
        return PostWebhookAsync(body, WebhookSignature.Sign(SubscribelyApiFactory.WebhookSecret, body, _factory.Time.GetUtcNow()));
    }

    private Task<PaymentEventStatus> LastEventStatusAsync() =>
        _factory.WithDbAsync<PaymentsDbContext, PaymentEventStatus>(db =>
            db.PaymentEvents.OrderByDescending(e => e.ReceivedAt).ThenByDescending(e => e.Id).Select(e => e.Status).FirstAsync(Ct));
}
