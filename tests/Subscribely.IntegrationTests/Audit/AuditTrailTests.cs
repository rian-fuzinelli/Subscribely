using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Subscribely.Infrastructure.Auditing;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.Modules.Audit.Persistence;
using Subscribely.Modules.Billing.Domain.Plans;
using Subscribely.Modules.Billing.Persistence;

namespace Subscribely.IntegrationTests.Audit;

public sealed class AuditTrailTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EntityChange_ViaApi_IsAuditedWithActorAndCorrelation()
    {
        var code = "a-" + Guid.NewGuid().ToString("N")[..10];
        var adminId = Guid.NewGuid();
        var client = _factory.CreateClient().WithBearer(TestTokens.Issue(Roles.PlatformAdmin, userId: adminId));
        var correlationId = "audit-" + Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        (await client.PostAsJsonAsync("/admin/plans", new { code, name = "Audited", monthlyPriceCents = 100, annualPriceCents = 1000, requestQuotaMonthly = 10, maxSeats = 1 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var scope = _factory.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var planEntry = await audit.Entries.AsNoTracking()
            .FirstAsync(e => e.Action == "plan.created" && e.CorrelationId == correlationId, Ct);

        planEntry.ActorType.ShouldBe(ActorType.User);
        planEntry.ActorId.ShouldBe(adminId.ToString());
        planEntry.After.ShouldNotBeNull().ShouldContain(code);
        planEntry.Before.ShouldBeNull();
        (await audit.Entries.AnyAsync(e => e.Action == "plan_version.created" && e.CorrelationId == correlationId, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task ExplicitRecord_IsWrittenWithNextSaveChanges()
    {
        var marker = Guid.NewGuid().ToString("N");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystem("audit-test");
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record("test.explicit", "Probe", marker, new { marker });
            var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            billing.Plans.Add(Plan.Create("x-" + marker[..12], new PlanTerms("X", 1, 1, 1, 1), DateTimeOffset.UtcNow).Value);
            await billing.SaveChangesAsync(Ct);
        }

        var entry = await FindAsync(e => e.Action == "test.explicit" && e.EntityId == marker);
        entry.ShouldNotBeNull().ActorType.ShouldBe(ActorType.System);
        entry.ActorId.ShouldBe("audit-test");
    }

    [Fact]
    public async Task AuditFailure_RollsBackBusinessChange()
    {
        var code = "r-" + Guid.NewGuid().ToString("N")[..12];

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IAuditLog>().Record(new string('x', AuditEntry.MaxActionLength + 1));
            var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            billing.Plans.Add(Plan.Create(code, new PlanTerms("R", 1, 1, 1, 1), DateTimeOffset.UtcNow).Value);
            await Should.ThrowAsync<DbUpdateException>(() => billing.SaveChangesAsync(Ct));
        }

        await using var check = _factory.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<BillingDbContext>().Plans.AnyAsync(p => p.Code == code, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateOrDelete_IsRejectedByTrigger()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAuditLog>().WriteAsync("test.immutable", cancellationToken: Ct);
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var update = await Should.ThrowAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("UPDATE audit.entries SET action = 'tampered' WHERE action = 'test.immutable'", Ct));
        var delete = await Should.ThrowAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM audit.entries WHERE action = 'test.immutable'", Ct));

        update.MessageText.ShouldBe("audit entries are append-only");
        delete.MessageText.ShouldBe("audit entries are append-only");
    }

    [Fact]
    public async Task TenantQuery_ReturnsOnlyOwnEntries_AndPaginates()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var log = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            for (var i = 0; i < 55; i++)
            {
                await log.WriteAsync("test.tenant", "Probe", i.ToString(System.Globalization.CultureInfo.InvariantCulture), tenantId: tenantA, cancellationToken: Ct);
            }

            await log.WriteAsync("test.tenant", "Probe", "other", tenantId: tenantB, cancellationToken: Ct);
        }

        var client = _factory.CreateClient().WithBearer(TestTokens.Issue(Roles.TenantAdmin, tenantA));
        var first = await client.GetFromJsonAsync<Page>("/v1/audit", Ct);
        var second = await client.GetFromJsonAsync<Page>($"/v1/audit?cursor={Uri.EscapeDataString(first!.NextCursor!)}", Ct);

        first.Items.Count.ShouldBe(50);
        second!.Items.Count.ShouldBe(5);
        second.NextCursor.ShouldBeNull();
        first.Items.Concat(second.Items).ShouldAllBe(e => e.TenantId == tenantA);
        first.Items.Concat(second.Items).Select(e => e.Id).Distinct().Count().ShouldBe(55);
    }

    [Fact]
    public async Task TenantQuery_AsMember_IsForbidden()
    {
        var client = _factory.CreateClient().WithBearer(TestTokens.Issue(Roles.Member, Guid.NewGuid()));

        (await client.GetAsync(new Uri("/v1/audit", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminQuery_FiltersByTenant()
    {
        var tenant = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditLog>().WriteAsync("test.admin", tenantId: tenant, cancellationToken: Ct);
        }

        var page = await _factory.CreateClient().AsPlatformAdmin().GetFromJsonAsync<Page>($"/admin/audit?tenantId={tenant}", Ct);

        page!.Items.ShouldHaveSingleItem().Action.ShouldBe("test.admin");
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<AuditEntry?> FindAsync(System.Linq.Expressions.Expression<Func<AuditEntry, bool>> predicate)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Entries.AsNoTracking().FirstOrDefaultAsync(predicate, Ct);
    }

    private sealed record Page(List<Item> Items, string? NextCursor);

    private sealed record Item(Guid Id, Guid? TenantId, string Action);
}
