using System.Collections.Concurrent;
using System.Net;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.IntegrationTests.Infrastructure;

namespace Subscribely.IntegrationTests.Foundation;

public sealed class HangfireTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(containers, s => s["Jobs:ServerEnabled"] = "true");

    [Fact]
    public async Task EnqueuedJob_IsExecutedByServer()
    {
        var marker = Guid.NewGuid();
        _ = _factory.CreateClient();

        _factory.Services.GetRequiredService<IBackgroundJobClient>().Enqueue(() => ProbeJob.Run(marker));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!ProbeJob.Executed.ContainsKey(marker) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        ProbeJob.Executed.ShouldContainKey(marker);
    }

    [Fact]
    public async Task Dashboard_WithoutPlatformAdmin_IsForbidden()
    {
        var response = await _factory.CreateClient().GetAsync(new Uri("/hangfire", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}

public static class ProbeJob
{
    public static ConcurrentDictionary<Guid, bool> Executed { get; } = new();

    public static void Run(Guid marker) => Executed.TryAdd(marker, true);
}
