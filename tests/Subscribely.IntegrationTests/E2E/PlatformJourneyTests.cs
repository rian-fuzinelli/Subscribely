using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.IntegrationTests.Infrastructure.Scenario;
using Subscribely.Modules.Payments.Domain;
using Subscribely.Modules.Payments.Persistence;

namespace Subscribely.IntegrationTests.E2E;

/// <summary>Cenários 6, 7, 8 e 10 da spec 09: idempotência, isolamento, quota e recuperação de senha.</summary>
public sealed partial class PlatformJourneyTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers);
    private readonly MailpitClient _mailpit = new(containers);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scenario6_WebhookReplayedThreeTimes_HasASingleEffect()
    {
        var tenant = await ActiveAsync();
        // O id do evento do gateway não é exposto pela API do produto: o cenário o lê como o operador leria no painel.
        var eventId = await _factory.WithDbAsync<PaymentsDbContext, string>(db => db.PaymentEvents.Select(e => e.ProviderEventId).SingleAsync(Ct));

        for (var i = 0; i < 3; i++)
        {
            (await _factory.CreateClient().PostAsync(new Uri($"/dev/fake-gateway/events/{eventId}/replay", UriKind.Relative), null, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await _factory.SettleAsync();

        (await _factory.WithDbAsync<PaymentsDbContext, int>(db => db.Charges.CountAsync(c => c.Status == ChargeStatus.Succeeded, Ct))).ShouldBe(1);
        (await _factory.WithDbAsync<PaymentsDbContext, int>(db => db.PaymentEvents.CountAsync(Ct))).ShouldBe(1);
        (await tenant.InvoicesAsync()).ShouldHaveSingleItem().GetProperty("status").GetString().ShouldBe("Paid");
        (await SubjectsAsync(tenant.AdminEmail)).Count(s => s.StartsWith("Pagamento confirmado", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task Scenario7_TenantB_CannotReadChangeOrListAnythingOfTenantA()
    {
        var a = await ActiveAsync();
        var b = await ActiveAsync();
        await a.CreatesApiKeyAsync();
        await b.CreatesApiKeyAsync();
        var projectA = await a.CreatesProjectAsync("segredo de A");
        var invoiceA = (await a.InvoicesAsync()).Single().GetProperty("id").GetGuid();
        var invitationA = await a.InvitesAsync($"a-{Guid.NewGuid():N}@x.test");

        // JWT do admin de B.
        (await b.Admin.GetAsync(new Uri($"/v1/invoices/{invoiceA}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.Admin.PostAsync(new Uri($"/v1/invoices/{invoiceA}/pay", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.Admin.DeleteAsync(new Uri($"/v1/users/invitations/{invitationA}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.InvoicesAsync()).ShouldAllBe(i => i.GetProperty("id").GetGuid() != invoiceA);
        (await b.Admin.GetFromJsonAsync<List<JsonElement>>("/v1/users", Ct))!.ShouldAllBe(u => u.GetProperty("email").GetString() == b.AdminEmail);
        (await b.Admin.GetFromJsonAsync<List<JsonElement>>("/v1/users/invitations", Ct))!.ShouldBeEmpty();
        (await b.PostsAsync("/v1/subscription/cancel")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await a.SubscriptionAsync()).GetProperty("cancelAtPeriodEnd").GetBoolean().ShouldBeFalse();

        // API key de B no produto (banco de B): o projeto de A não existe.
        (await b.Product!.GetAsync(new Uri($"/v1/projects/{projectA}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.Product.PutAsJsonAsync($"/v1/projects/{projectA}", new { name = "invadido" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.Product.DeleteAsync(new Uri($"/v1/projects/{projectA}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.Product.GetFromJsonAsync<List<JsonElement>>("/v1/projects", Ct))!.ShouldBeEmpty();
        (await a.Product!.GetFromJsonAsync<JsonElement>($"/v1/projects/{projectA}", Ct)).GetProperty("name").GetString().ShouldBe("segredo de A");
    }

    [Fact]
    public async Task Scenario8_QuotaReached_Returns429WithHeaders_AndAlertsGoOnceToAdminsOnly()
    {
        var tenant = await Journey.SignsUpAsync(_factory, Plans.Starter);
        var member = await tenant.AddsMemberAsync();
        await tenant.CreatesApiKeyAsync();

        (await tenant.CallsProductAsync(100)).ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        (await tenant.CallsProductAsync(2)).ShouldAllBe(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        await _factory.SettleAsync();

        var last = (await tenant.CallsProductAsync(1)).Single();
        last.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        last.Headers.GetValues("X-RateLimit-Limit").Single().ShouldBe("100");
        last.Headers.GetValues("X-RateLimit-Remaining").Single().ShouldBe("0");
        last.Headers.Contains("X-RateLimit-Reset").ShouldBeTrue();
        last.Headers.RetryAfter!.Delta!.Value.ShouldBeGreaterThan(TimeSpan.Zero);

        var subjects = await SubjectsAsync(tenant.AdminEmail);
        subjects.Count(s => s.Contains("80% das requisições", StringComparison.Ordinal)).ShouldBe(1);
        subjects.Count(s => s.StartsWith("Limite de requisições", StringComparison.Ordinal)).ShouldBe(1);
        (await SubjectsAsync(member)).ShouldNotContain(s => s.Contains("requisições", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scenario10_PasswordReset_ByEmail_ThenLoginWithTheNewPassword_AndTheOldRefreshTokenIsRevoked()
    {
        var tenant = await Journey.SignsUpAsync(_factory);
        const string newPassword = "Brand-New-Password-2026!";

        (await _factory.CreateClient().PostAsJsonAsync("/v1/auth/password-reset", new { email = tenant.AdminEmail }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await _factory.SettleAsync();

        var text = (await _mailpit.SingleToAsync(tenant.AdminEmail, "Redefinição")).GetProperty("Text").GetString()!;
        var token = Uri.UnescapeDataString(ResetToken().Match(text).Groups[1].Value);
        token.ShouldNotBeNullOrWhiteSpace();

        (await _factory.CreateClient().PostAsJsonAsync("/v1/auth/password-reset/confirm", new { token, newPassword }, Ct))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        (await Given.LoginAsync(_factory, tenant.AdminEmail, newPassword)).AccessToken.ShouldNotBeNullOrWhiteSpace();
        (await _factory.CreateClient().PostAsJsonAsync("/v1/auth/login", new { email = tenant.AdminEmail, password = TenantScenario.DefaultPassword }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _factory.CreateClient().PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = tenant.Tenant.Tokens.RefreshToken }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<Journey> ActiveAsync()
    {
        var tenant = await Journey.SignsUpAsync(_factory);
        await tenant.RegistersCardAsync("pm_card_ok");
        await _factory.AdvanceAsync(TimeSpan.FromDays(14));
        (await tenant.StatusAsync()).ShouldBe("Active");
        return tenant;
    }

    private async Task<List<string>> SubjectsAsync(string address) =>
        (await _mailpit.MessagesToAsync(address)).Select(m => m.GetProperty("Subject").GetString()!).ToList();

    [GeneratedRegex(@"password-reset\?token=(\S+)")]
    private static partial Regex ResetToken();
}
