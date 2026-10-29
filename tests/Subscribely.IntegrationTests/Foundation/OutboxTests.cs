using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.Infrastructure;
using Subscribely.Infrastructure.Outbox;
using Subscribely.Infrastructure.Persistence;
using Subscribely.IntegrationTests.Infrastructure;
using Subscribely.SharedKernel;

namespace Subscribely.IntegrationTests.Foundation;

public sealed class OutboxTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers, configureServices: services =>
    {
        services.AddModuleDbContext<SampleDbContext>(SampleDbContext.SchemaName);
        services.AddScoped<IIntegrationEventHandler<SampleHappened>, RecordingHandler>();
        services.AddScoped<IIntegrationEventHandler<SampleFailing>, FailingHandler>();
    });

    public async ValueTask InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE SCHEMA IF NOT EXISTS itest; CREATE TABLE IF NOT EXISTS itest.samples (id uuid PRIMARY KEY, name text NOT NULL UNIQUE);",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SaveChanges_WritesEventToOutbox_AndProcessorDispatchesIt()
    {
        var marker = Guid.NewGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            db.Samples.Add(Sample.Create($"s-{marker}", new SampleHappened(marker)));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await FindMessageAsync(marker)).ShouldNotBeNull().ProcessedAt.ShouldBeNull();

        await DrainAsync();

        RecordingHandler.Received.ShouldContainKey(marker);
        (await FindMessageAsync(marker))!.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task RolledBackTransaction_LeavesNoOutboxMessage()
    {
        var marker = Guid.NewGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            db.Samples.Add(Sample.Create($"s-{marker}", new SampleHappened(marker)));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            await tx.RollbackAsync(TestContext.Current.CancellationToken);
        }

        (await FindMessageAsync(marker)).ShouldBeNull();
    }

    [Fact]
    public async Task FailedSaveChanges_LeavesNoOutboxMessage()
    {
        var marker = Guid.NewGuid();
        var name = $"dup-{marker}";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            db.Samples.Add(Sample.Create(name, new SampleHappened(Guid.NewGuid())));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            db.Samples.Add(Sample.Create(name, new SampleHappened(marker)));
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        (await FindMessageAsync(marker)).ShouldBeNull();
    }

    [Fact]
    public async Task FailingHandler_StopsAfterFiveAttempts()
    {
        var marker = Guid.NewGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            db.Publish(new SampleFailing(marker));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        for (var i = 0; i < 8; i++)
        {
            await DrainAsync();
        }

        var message = (await FindMessageAsync(marker)).ShouldNotBeNull();
        message.Attempts.ShouldBe(OutboxMessage.MaxAttempts);
        message.ProcessedAt.ShouldBeNull();
        message.LastError.ShouldNotBeNull().ShouldContain("handler failure");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task DrainAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().DrainAsync(TestContext.Current.CancellationToken);
    }

    private async Task<OutboxMessage?> FindMessageAsync(Guid marker)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var pattern = $"%{marker}%";
        return await db.Messages
            .FromSql($"SELECT * FROM outbox.messages WHERE payload::text LIKE {pattern}")
            .AsNoTracking()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
    }

    public sealed record SampleHappened(Guid Marker) : IIntegrationEvent;

    public sealed record SampleFailing(Guid Marker) : IIntegrationEvent;

    private sealed class RecordingHandler : IIntegrationEventHandler<SampleHappened>
    {
        public static ConcurrentDictionary<Guid, int> Received { get; } = new();

        public Task HandleAsync(SampleHappened @event, CancellationToken cancellationToken)
        {
            Received.AddOrUpdate(@event.Marker, 1, (_, count) => count + 1);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingHandler : IIntegrationEventHandler<SampleFailing>
    {
        public Task HandleAsync(SampleFailing @event, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("handler failure");
    }

    private sealed class Sample : Entity
    {
        private Sample()
        {
        }

        public string Name { get; private set; } = string.Empty;

        public static Sample Create(string name, IIntegrationEvent @event)
        {
            var sample = new Sample { Name = name };
            sample.Raise(@event);
            return sample;
        }
    }

    private sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : ModuleDbContext(options)
    {
        public const string SchemaName = "itest";

        public override string Schema => SchemaName;

        public DbSet<Sample> Samples => Set<Sample>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Sample>(b =>
            {
                b.ToTable("samples");
                b.HasKey(s => s.Id);
                b.Ignore(s => s.Events);
            });
        }
    }
}
