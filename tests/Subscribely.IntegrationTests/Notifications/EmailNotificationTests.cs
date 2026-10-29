using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Features.Subscriptions.Jobs;
using Subscribely.Modules.Metering.Contracts;
using Subscribely.Modules.Notifications.Email;
using Subscribely.Modules.Notifications.Persistence;

namespace Subscribely.IntegrationTests.Notifications;

/// <summary>Lê as mensagens recebidas pelo Mailpit (compartilhado pela suíte; o filtro é o destinatário único do teste).</summary>
public sealed class Mailpit(ContainersFixture containers)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task<List<JsonElement>> MessagesToAsync(string address)
    {
        using var http = new HttpClient { BaseAddress = containers.MailpitApiBaseAddress };
        var result = await http.GetFromJsonAsync<JsonElement>($"api/v1/search?query={Uri.EscapeDataString($"to:\"{address}\"")}", Ct);
        return result.GetProperty("messages").EnumerateArray().ToList();
    }

    public async Task<JsonElement> SingleToAsync(string address, string subjectContains)
    {
        var message = (await MessagesToAsync(address)).Where(m => m.GetProperty("Subject").GetString()!.Contains(subjectContains, StringComparison.Ordinal)).ShouldHaveSingleItem();
        using var http = new HttpClient { BaseAddress = containers.MailpitApiBaseAddress };
        return await http.GetFromJsonAsync<JsonElement>($"api/v1/message/{message.GetProperty("ID").GetString()}", Ct);
    }
}

public sealed class EmailNotificationTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers);
    private readonly Mailpit _mailpit = new(containers);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Invitation_IsSentToTheInvitee_WithTheSingleUseLink_AndReprocessingDoesNotResend()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        var email = $"guest-{Guid.NewGuid():N}@x.test";
        (await tenant.AdminClient(_factory).PostAsJsonAsync("/v1/users/invitations", new { email, role = "Member" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await Given.OutboxEventAsync(_factory, "UserInvited", email);

        await _factory.DrainOutboxAsync();
        await _factory.HandleAsync(invited.Deserialize<Modules.Tenancy.Contracts.UserInvited>(JsonSerializerOptions.Web)!);

        var message = await _mailpit.SingleToAsync(email, "Convite");
        message.GetProperty("Text").GetString()!.ShouldContain($"/invitations/{invited.GetProperty("token").GetString()}");
        message.GetProperty("HTML").GetString()!.ShouldContain("lang=\"pt-BR\"");
        (await LogAsync(email)).ShouldHaveSingleItem().Status.ShouldBe(EmailStatus.Sent);
    }

    [Fact]
    public async Task PasswordReset_IsSentToTheUser_WithTheLinkOnly()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);

        (await _factory.CreateClient().PostAsJsonAsync("/v1/auth/password-reset", new { email = tenant.AdminEmail }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await _factory.DrainOutboxAsync();

        var text = (await _mailpit.SingleToAsync(tenant.AdminEmail, "Redefinição")).GetProperty("Text").GetString()!;
        text.ShouldContain("password-reset?token=");
        text.ShouldNotContain(TenantScenario.DefaultPassword);
    }

    [Fact]
    public async Task TrialEnding_GoesToActiveAdminsOnly_WithTheCompanyNameEncoded()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        var member = await AddMemberAsync(tenant);
        await RenameCompanyAsync(tenant, "<b>Acme</b> & Cia");

        await _factory.HandleAsync(new TrialEnding(tenant.TenantId, Guid.NewGuid(), _factory.Time.GetUtcNow().AddDays(3), _factory.Time.GetUtcNow()));

        var html = (await _mailpit.SingleToAsync(tenant.AdminEmail, "teste")).GetProperty("HTML").GetString()!;
        html.ShouldContain("&lt;b&gt;Acme&lt;/b&gt; &amp; Cia");
        html.ShouldNotContain("<b>Acme</b>");
        (await _mailpit.MessagesToAsync(member)).ShouldNotContain(m => m.GetProperty("Subject").GetString()!.Contains("teste", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvoiceIssuedAndPaid_AreSent_WithBrazilianCurrencyAndTime()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        await tenant.AdminClient(_factory).PutAsJsonAsync("/v1/billing/payment-method", new { token = "pm_card_ok" }, Ct);

        _factory.Time.Advance(TimeSpan.FromDays(14));
        await _factory.RunJobAsync<TrialExpiryJob>();
        await _factory.DrainOutboxAsync();
        await _factory.Jobs.RunDueAsync(DateTimeOffset.UtcNow.AddSeconds(10));
        await _factory.DrainOutboxAsync();

        var issued = (await _mailpit.SingleToAsync(tenant.AdminEmail, "emitida")).GetProperty("Text").GetString()!;
        issued.ShouldContain("R$ 49,00");
        issued.ShouldContain("29/01/2026 às 09:00", Case.Sensitive, "12:00 UTC is 09:00 in São Paulo");
        (await _mailpit.SingleToAsync(tenant.AdminEmail, "Pagamento confirmado")).GetProperty("Text").GetString()!.ShouldContain("R$ 49,00");
    }

    [Fact]
    public async Task PaymentFailedAndSuspension_AreSent_OncePerAttempt()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        await tenant.AdminClient(_factory).PutAsJsonAsync("/v1/billing/payment-method", new { token = "pm_card_declined" }, Ct);
        _factory.Time.Advance(TimeSpan.FromDays(14));
        await _factory.RunJobAsync<TrialExpiryJob>();
        await _factory.DrainOutboxAsync();
        await _factory.Jobs.RunDueAsync(DateTimeOffset.UtcNow.AddSeconds(10));
        await _factory.DrainOutboxAsync();

        var failed = (await _mailpit.SingleToAsync(tenant.AdminEmail, "Não conseguimos cobrar")).GetProperty("Text").GetString()!;
        failed.ShouldContain("cartão recusado");
        failed.ShouldContain("Vamos tentar de novo em 30/01/2026");

        var suspended = new Modules.Billing.Contracts.SubscriptionSuspended(tenant.TenantId, Guid.NewGuid(), _factory.Time.GetUtcNow());
        await _factory.HandleAsync(suspended);
        await _factory.HandleAsync(suspended);
        (await _mailpit.MessagesToAsync(tenant.AdminEmail)).Count(m => m.GetProperty("Subject").GetString() == "Acesso suspenso por falta de pagamento").ShouldBe(1);
    }

    [Fact]
    public async Task QuotaAlerts_GoToAdminsOnly_WithTheRightTemplate()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        var member = await AddMemberAsync(tenant);

        await _factory.HandleAsync(new QuotaThresholdReached(tenant.TenantId, "2026-01", 80, 1_000, _factory.Time.GetUtcNow()));
        await _factory.HandleAsync(new QuotaThresholdReached(tenant.TenantId, "2026-01", 100, 1_000, _factory.Time.GetUtcNow()));

        (await _mailpit.SingleToAsync(tenant.AdminEmail, "80%")).GetProperty("Text").GetString()!.ShouldContain("1.000");
        (await _mailpit.SingleToAsync(tenant.AdminEmail, "atingido")).GetProperty("Text").GetString()!.ShouldContain("31/01/2026 às 21:00", Case.Sensitive, "the UTC reset shown in São Paulo time");
        (await _mailpit.MessagesToAsync(member)).ShouldNotContain(m => m.GetProperty("Subject").GetString()!.Contains("requisições", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZeroTotalInvoice_SendsNothing()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);

        await _factory.HandleAsync(new InvoiceIssued(Guid.NewGuid(), tenant.TenantId, Guid.NewGuid(), "SUB-2026-000000", "Upgrade", 0, _factory.Time.GetUtcNow()));

        (await LogAsync(tenant.AdminEmail)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AdminLog_IsPlatformAdminOnly_AndFiltersByTenantAndStatus()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        await _factory.HandleAsync(new QuotaThresholdReached(tenant.TenantId, "2026-01", 80, 10, _factory.Time.GetUtcNow()));

        (await tenant.AdminClient(_factory).GetAsync(new Uri("/admin/notifications", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var rows = await _factory.CreateClient().AsPlatformAdmin().GetFromJsonAsync<List<JsonElement>>($"/admin/notifications?tenantId={tenant.TenantId}&status=Sent", Ct);
        rows!.ShouldHaveSingleItem().GetProperty("template").GetString().ShouldBe("quota-80");
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private Task<List<EmailLog>> LogAsync(string address) =>
        _factory.WithDbAsync<NotificationsDbContext, List<EmailLog>>(db => db.EmailLog.Where(e => e.ToAddress == address).ToListAsync(Ct));

    private async Task<string> AddMemberAsync(TenantScenario tenant)
    {
        var email = $"member-{Guid.NewGuid():N}@x.test";
        await tenant.AdminClient(_factory).PostAsJsonAsync("/v1/users/invitations", new { email, role = "Member" }, Ct);
        var token = (await Given.OutboxEventAsync(_factory, "UserInvited", email)).GetProperty("token").GetString();
        (await _factory.CreateClient().PostAsJsonAsync($"/v1/invitations/{token}/accept", new { password = TenantScenario.DefaultPassword }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await _factory.DrainOutboxAsync();
        return email;
    }

    private Task RenameCompanyAsync(TenantScenario tenant, string name) =>
        _factory.WithDbAsync<Modules.Tenancy.Persistence.TenancyDbContext, int>(db =>
            db.Database.ExecuteSqlAsync($"UPDATE tenancy.tenants SET name = {name} WHERE id = {tenant.TenantId}", Ct));
}

/// <summary>SMTP inacessível: o e-mail fica <c>Failed</c>, o consumidor lança para o outbox reprocessar e para após 5 tentativas.</summary>
public sealed class EmailFailureTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers, settings =>
    {
        settings["Smtp:Port"] = "1";
        settings["Smtp:Timeout"] = "00:00:02";
    });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SmtpDown_MarksFailed_RetriesViaOutbox_AndGivesUpAfter5Attempts()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        var alert = new QuotaThresholdReached(tenant.TenantId, "2026-01", 80, 10, _factory.Time.GetUtcNow());

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await Should.ThrowAsync<EmailDeliveryException>(() => _factory.HandleAsync(alert));
        }

        await _factory.HandleAsync(alert); // tentativas esgotadas: não lança nem tenta de novo

        var log = await _factory.WithDbAsync<NotificationsDbContext, EmailLog>(db => db.EmailLog.SingleAsync(e => e.TenantId == tenant.TenantId, Ct));
        log.Status.ShouldBe(EmailStatus.Failed);
        log.Attempts.ShouldBe(5);
        log.LastError.ShouldNotBeNullOrWhiteSpace();
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
