using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.IntegrationTests.Infrastructure.Scenario;

namespace Subscribely.IntegrationTests.E2E;

/// <summary>Cenários 1–5 e 9 da spec 09: o ciclo de vida da assinatura de ponta a ponta, só pela API HTTP.</summary>
public sealed class BillingJourneyTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers);
    private readonly MailpitClient _mailpit = new(containers);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scenario1_HappyPath_TrialToPaidInvoice_WithEmails()
    {
        var tenant = await Journey.SignsUpAsync(_factory);
        (await tenant.StatusAsync()).ShouldBe("Trialing");

        await tenant.CreatesApiKeyAsync();
        (await tenant.CallsProductAsync(10)).ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        await tenant.RegistersCardAsync("pm_card_ok");

        await _factory.AdvanceAsync(TimeSpan.FromDays(11));
        await _factory.AdvanceAsync(TimeSpan.FromDays(3));

        (await tenant.StatusAsync()).ShouldBe("Active");
        var invoice = (await tenant.InvoicesAsync()).ShouldHaveSingleItem();
        invoice.GetProperty("reason").GetString().ShouldBe("TrialConversion");
        invoice.GetProperty("status").GetString().ShouldBe("Paid");
        invoice.GetProperty("totalCents").GetInt64().ShouldBe(4_900);

        var subjects = await SubjectsAsync(tenant.AdminEmail);
        subjects.ShouldContain(s => s.StartsWith("Seu teste do Subscribely termina", StringComparison.Ordinal));
        subjects.ShouldContain(s => s.Contains("emitida: R$ 49,00", StringComparison.Ordinal));
        subjects.ShouldContain(s => s.StartsWith("Pagamento confirmado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scenario2_Upgrade_MidPeriod_IsProrated_AndTheNewQuotaAppliesAtOnce()
    {
        var tenant = await ActiveAsync(Plans.Starter, "pm_card_ok");
        var period = await tenant.SubscriptionAsync();
        var start = period.GetProperty("currentPeriodStart").GetDateTimeOffset();
        await _factory.TravelToAsync(start + ((period.GetProperty("currentPeriodEnd").GetDateTimeOffset() - start) / 2));

        var changed = await tenant.ChangesPlanAsync(Plans.Pro);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await changed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("effective").GetString().ShouldBe("immediate");
        await _factory.SettleAsync();

        var upgrade = await tenant.InvoiceAsync(body.GetProperty("invoiceId").GetGuid());
        upgrade.GetProperty("reason").GetString().ShouldBe("Upgrade");
        upgrade.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("amountCents").GetInt64()).ShouldBe([-2_450, 7_450]);
        upgrade.GetProperty("totalCents").GetInt64().ShouldBe(5_000);
        upgrade.GetProperty("status").GetString().ShouldBe("Paid");

        await tenant.CreatesApiKeyAsync();
        (await tenant.CallsProductAsync(1)).Single().Headers.GetValues("X-RateLimit-Limit").Single().ShouldBe("1000");
    }

    [Fact]
    public async Task Scenario3_Downgrade_BlockedBySeats_ThenScheduled_AndAppliedAtPeriodEnd()
    {
        var tenant = await ActiveAsync(Plans.Pro, "pm_card_ok");
        var invitations = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            invitations.Add(await tenant.InvitesAsync($"seat-{i}-{Guid.NewGuid():N}@x.test"));
        }

        var blocked = await tenant.ChangesPlanAsync(Plans.Starter);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("seats-exceed-target-plan");
        problem.GetProperty("activeSeats").GetInt32().ShouldBe(4);
        problem.GetProperty("targetMaxSeats").GetInt32().ShouldBe(3);

        (await tenant.Admin.DeleteAsync(new Uri($"/v1/users/invitations/{invitations[0]}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var scheduled = await tenant.ChangesPlanAsync(Plans.Starter);
        (await scheduled.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("effective").GetString().ShouldBe("period_end");
        var subscription = await tenant.SubscriptionAsync();
        subscription.GetProperty("plan").GetProperty("code").GetString().ShouldBe(Plans.Pro);
        subscription.GetProperty("pendingChange").GetProperty("planCode").GetString().ShouldBe(Plans.Starter);

        await _factory.TravelToAsync(subscription.GetProperty("currentPeriodEnd").GetDateTimeOffset());

        (await tenant.SubscriptionAsync()).GetProperty("plan").GetProperty("code").GetString().ShouldBe(Plans.Starter);
        var renewal = (await tenant.InvoicesAsync()).First(i => i.GetProperty("reason").GetString() == "Renewal");
        renewal.GetProperty("totalCents").GetInt64().ShouldBe(4_900);
        renewal.GetProperty("status").GetString().ShouldBe("Paid");
    }

    [Fact]
    public async Task Scenario4_Cancel_Resume_Cancel_ThenPeriodEnd_BlocksTheProduct()
    {
        var tenant = await ActiveAsync(Plans.Starter, "pm_card_ok");
        await tenant.CreatesApiKeyAsync();

        (await tenant.PostsAsync("/v1/subscription/cancel")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await tenant.PostsAsync("/v1/subscription/resume")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await tenant.PostsAsync("/v1/subscription/cancel")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await tenant.CallsProductAsync(1)).Single().StatusCode.ShouldBe(HttpStatusCode.OK, "canceling only takes effect at the period end");

        await _factory.TravelToAsync((await tenant.SubscriptionAsync()).GetProperty("currentPeriodEnd").GetDateTimeOffset());

        (await tenant.StatusAsync()).ShouldBe("Canceled");
        var blocked = (await tenant.CallsProductAsync(1)).Single();
        blocked.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        (await blocked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("subscription-inactive");
        (await tenant.InvoicesAsync()).ShouldNotContain(i => i.GetProperty("reason").GetString() == "Renewal");
    }

    [Fact]
    public async Task Scenario5_Dunning_DeclinedCard_GoesToSuspended_AndANewCardReactivates()
    {
        var tenant = await ActiveAsync(Plans.Starter, "pm_card_declined", expectedStatus: "PastDue");
        var issuedAt = (await tenant.InvoicesAsync()).Single().GetProperty("issuedAt").GetDateTimeOffset();

        await _factory.TravelToAsync(issuedAt.AddDays(1));
        (await tenant.StatusAsync()).ShouldBe("PastDue");
        await _factory.TravelToAsync(issuedAt.AddDays(3));
        (await tenant.StatusAsync()).ShouldBe("PastDue");
        await _factory.TravelToAsync(issuedAt.AddDays(7));
        (await tenant.StatusAsync()).ShouldBe("Suspended");

        await tenant.RegistersCardAsync("pm_card_ok");
        await _factory.SettleAsync();

        (await tenant.StatusAsync()).ShouldBe("Active");
        (await tenant.InvoicesAsync()).Single().GetProperty("status").GetString().ShouldBe("Paid");
        (await SubjectsAsync(tenant.AdminEmail)).Count(s => s.StartsWith("Não conseguimos cobrar", StringComparison.Ordinal)).ShouldBe(3);
        (await SubjectsAsync(tenant.AdminEmail)).ShouldContain("Acesso suspenso por falta de pagamento");
    }

    [Fact]
    public async Task Scenario9_LongSuspension_CancelsAndVoids_ThenReactivationKeepsTheData()
    {
        var tenant = await ActiveAsync(Plans.Starter, "pm_card_declined", expectedStatus: "PastDue");
        await tenant.CreatesApiKeyAsync();
        var project = await tenant.CreatesProjectAsync("guardado");
        var invoice = (await tenant.InvoicesAsync()).Single();
        var issuedAt = invoice.GetProperty("issuedAt").GetDateTimeOffset();
        foreach (var day in new[] { 1, 3, 7 })
        {
            await _factory.TravelToAsync(issuedAt.AddDays(day));
        }

        (await tenant.StatusAsync()).ShouldBe("Suspended");

        await _factory.AdvanceAsync(TimeSpan.FromDays(30));

        (await tenant.StatusAsync()).ShouldBe("Canceled");
        var voided = await tenant.InvoiceAsync(invoice.GetProperty("id").GetGuid());
        voided.GetProperty("status").GetString().ShouldBe("Void");
        voided.GetProperty("voidReason").GetString().ShouldBe("suspension-expired");

        await tenant.RegistersCardAsync("pm_card_ok");
        (await tenant.PostsAsync("/v1/subscription/reactivate")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await _factory.SettleAsync();

        (await tenant.StatusAsync()).ShouldBe("Active");
        (await tenant.InvoicesAsync()).ShouldContain(i => i.GetProperty("reason").GetString() == "Reactivation" && i.GetProperty("status").GetString() == "Paid");
        (await tenant.Product!.GetFromJsonAsync<JsonElement>($"/v1/projects/{project}", Ct)).GetProperty("name").GetString().ShouldBe("guardado");
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>Signup → cartão → fim do trial (14 dias) → conversão cobrada.</summary>
    private async Task<Journey> ActiveAsync(string plan, string card, string expectedStatus = "Active")
    {
        var tenant = await Journey.SignsUpAsync(_factory, plan);
        await tenant.RegistersCardAsync(card);
        await _factory.AdvanceAsync(TimeSpan.FromDays(14));
        (await tenant.StatusAsync()).ShouldBe(expectedStatus);
        return tenant;
    }

    private async Task<List<string>> SubjectsAsync(string address) =>
        (await _mailpit.MessagesToAsync(address)).Select(m => m.GetProperty("Subject").GetString()!).ToList();
}
