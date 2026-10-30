using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.Modules.Tenancy.Domain;
using Subscribely.Modules.Tenancy.Features.Signup;
using Subscribely.Modules.Tenancy.Persistence;

namespace Subscribely.IntegrationTests.Tenancy;

public sealed class SignupAndAuthTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Signup_ProvisionsDatabase_AndActivatesTenant()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);

        var stored = await _factory.WithDbAsync<TenancyDbContext, Tenant?>(db => db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenant.TenantId, Ct));
        stored.ShouldNotBeNull().Status.ShouldBe(TenantStatus.Active);
        (await DatabaseExistsAsync(stored.DatabaseName)).ShouldBeTrue();
        var provisioned = await Given.OutboxEventAsync(_factory, "TenantProvisioned", tenant.TenantId.ToString());
        provisioned.GetProperty("planCode").GetString().ShouldBe("starter");
    }

    [Fact]
    public async Task Signup_BeforeProvisioning_LoginIsForbidden()
    {
        await Given.EnsurePlanAsync(_factory, "starter");
        var slug = Given.NewSlug();
        (await Given.SignUpAsync(_factory, slug, $"a@{slug}.test")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var login = await _factory.CreateClient().PostAsJsonAsync("/v1/auth/login", new { email = $"a@{slug}.test", password = TenantScenario.DefaultPassword }, Ct);

        login.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SignupStatus_ShowsProvisioningThenActive_AndUnknownIs404()
    {
        await Given.EnsurePlanAsync(_factory, "starter");
        var slug = Given.NewSlug();
        var signup = await Given.SignUpAsync(_factory, slug, $"s@{slug}.test");
        var tenantId = (await signup.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("tenantId").GetGuid();
        var anonymous = _factory.CreateClient();

        (await anonymous.GetFromJsonAsync<JsonElement>($"/v1/signup/{tenantId}", Ct)).GetProperty("status").GetString().ShouldBe("Provisioning");
        await _factory.DrainOutboxAsync();
        (await anonymous.GetFromJsonAsync<JsonElement>($"/v1/signup/{tenantId}", Ct)).GetProperty("status").GetString().ShouldBe("Active");
        (await anonymous.GetAsync(new Uri($"/v1/signup/{Guid.NewGuid()}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Signup_DuplicateSlugOrEmail_Returns409()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);

        (await Given.SignUpAsync(_factory, tenant.Slug, "other@x.test")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Given.SignUpAsync(_factory, Given.NewSlug(), tenant.AdminEmail)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Signup_UnknownPlan_Returns422_AndInvalidBody_Returns400()
    {
        (await Given.SignUpAsync(_factory, Given.NewSlug(), "x@x.test", plan: "does-not-exist")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Given.SignUpAsync(_factory, "Bad Slug", "x@x.test")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Given.SignUpAsync(_factory, Given.NewSlug(), "x@x.test", password: "short")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Provisioning_IsIdempotent_WhenDatabaseAlreadyExists()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenantDatabaseProvisioner>().ProvisionAsync(tenant.Slug, Ct);
    }

    [Fact]
    public async Task Provisioning_FailingThreeTimes_MarksTenantAsFailed()
    {
        await using var broken = new SubscribelyApiFactory(containers, s => s["ConnectionStrings:PostgresAdmin"] = "Host=localhost;Port=1;Database=postgres;Username=x;Password=x;Timeout=1");
        var slug = Given.NewSlug();
        var tenantId = await broken.WithDbAsync<TenancyDbContext, Guid>(async db =>
        {
            // Sem o evento TenantSignedUp: o outbox é compartilhado e outra factory provisionaria este tenant.
            var tenant = Tenant.SignUp(slug, "Broken", "subscribely_t_" + slug.Replace('-', '_'), "starter", "monthly", DateTimeOffset.UtcNow);
            tenant.ClearEvents();
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(Ct);
            return tenant.Id;
        });

        for (var attempt = 1; attempt <= Tenant.MaxProvisioningAttempts; attempt++)
        {
            await using var scope = broken.Services.CreateAsyncScope();
            var run = () => scope.ServiceProvider.GetRequiredService<ProvisionTenantJob>().RunAsync(tenantId, Ct);
            if (attempt < Tenant.MaxProvisioningAttempts)
            {
                await Should.ThrowAsync<Exception>(run);
            }
            else
            {
                await run();
            }
        }

        var status = await broken.WithDbAsync<TenancyDbContext, TenantStatus>(db => db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId).Select(t => t.Status).SingleAsync(Ct));
        status.ShouldBe(TenantStatus.ProvisioningFailed);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401Generic()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);

        var wrong = await _factory.CreateClient().PostAsJsonAsync("/v1/auth/login", new { email = tenant.AdminEmail, password = "wrong-password-123" }, Ct);
        var unknown = await _factory.CreateClient().PostAsJsonAsync("/v1/auth/login", new { email = "nobody@nowhere.test", password = "wrong-password-123" }, Ct);

        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var wrongBody = await wrong.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
        var unknownBody = await unknown.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
        wrongBody.GetProperty("code").GetString().ShouldBe(unknownBody.GetProperty("code").GetString(), "same error (no user enumeration)");
        wrongBody.GetProperty("detail").GetString().ShouldBe(unknownBody.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Login_TooManyAttempts_Returns429()
    {
        var email = $"rate-{Guid.NewGuid():N}@x.test";
        HttpStatusCode last = default;
        for (var i = 0; i < 6; i++)
        {
            last = (await _factory.CreateClient().PostAsJsonAsync("/v1/auth/login", new { email, password = "whatever-password" }, Ct)).StatusCode;
        }

        last.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Refresh_Rotates_AndReuseRevokesWholeFamily()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        var client = _factory.CreateClient();

        var rotated = await client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = tenant.Tokens.RefreshToken }, Ct);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var next = (await rotated.Content.ReadFromJsonAsync<TokenPairDto>(Ct))!;
        next.RefreshToken.ShouldNotBe(tenant.Tokens.RefreshToken);

        (await client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = tenant.Tokens.RefreshToken }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = next.RefreshToken }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Expired_Returns401()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        _factory.Time.Advance(TimeSpan.FromDays(8));

        var response = await _factory.CreateClient().PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = tenant.Tokens.RefreshToken }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        var client = _factory.CreateClient();

        (await client.PostAsJsonAsync("/v1/auth/logout", new { refreshToken = tenant.Tokens.RefreshToken }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = tenant.Tokens.RefreshToken }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PasswordReset_ChangesPassword_AndRevokesSessions()
    {
        var tenant = await Given.ActiveTenantAsync(_factory);
        var client = _factory.CreateClient();

        (await client.PostAsJsonAsync("/v1/auth/password-reset", new { email = tenant.AdminEmail }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var token = (await Given.OutboxEventAsync(_factory, "PasswordResetRequested", tenant.AdminEmail)).GetProperty("token").GetString();

        const string newPassword = "Brand-New-Password-2026";
        (await client.PostAsJsonAsync("/v1/auth/password-reset/confirm", new { token, newPassword }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await Given.LoginAsync(_factory, tenant.AdminEmail, newPassword);
        (await client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = tenant.Tokens.RefreshToken }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/v1/auth/password-reset/confirm", new { token, newPassword }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PasswordReset_UnknownEmail_Returns202WithoutEvent()
    {
        var email = $"ghost-{Guid.NewGuid():N}@x.test";

        (await _factory.CreateClient().PostAsJsonAsync("/v1/auth/password-reset", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await Given.FindOutboxEventAsync(_factory, "PasswordResetRequested", email)).ShouldBeNull();
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<bool> DatabaseExistsAsync(string name)
    {
        await using var connection = new NpgsqlConnection(containers.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", connection);
        command.Parameters.AddWithValue("n", name);
        return await command.ExecuteScalarAsync(Ct) is not null;
    }
}
