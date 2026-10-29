using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.Infrastructure.Web;
using Subscribely.IntegrationTests.Infrastructure;

namespace Subscribely.IntegrationTests.Foundation;

public sealed class HealthAndPipelineTests(ContainersFixture containers) : IAsyncLifetime
{
    private readonly SubscribelyApiFactory _factory = new(
        containers,
        configureServices: s => s.AddSingleton<IStartupFilter, ThrowingEndpointStartupFilter>());

    [Fact]
    public async Task Live_ReturnsOk()
    {
        var response = await _factory.CreateClient().GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_WithPostgresAndRedisUp_ReturnsOk()
    {
        var response = await _factory.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
    }

    [Fact]
    public async Task Ready_WithRedisDown_ReturnsServiceUnavailable()
    {
        await using var factory = new SubscribelyApiFactory(
            containers,
            s => s["ConnectionStrings:Redis"] = "localhost:1,connectTimeout=200");

        var response = await factory.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails()
    {
        var response = await _factory.CreateClient().GetAsync(new Uri("/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task UnhandledException_ReturnsProblemDetailsWithoutStackTrace()
    {
        var response = await _factory.CreateClient().GetAsync(new Uri(ThrowingEndpointStartupFilter.Path, UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        body.ShouldNotContain("boom-secret-detail");
        body.ShouldNotContain("   at ");
    }

    [Fact]
    public async Task CorrelationId_IsEchoedWhenValid()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "abc-123");

        var response = await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single().ShouldBe("abc-123");
    }

    [Fact]
    public async Task CorrelationId_IsReplacedWhenInvalid()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, new string('x', 65));

        var response = await _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        var value = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        value.Length.ShouldBe(32);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>Acrescenta, depois do pipeline da aplicação, um endpoint que lança exceção.</summary>
    private sealed class ThrowingEndpointStartupFilter : IStartupFilter
    {
        public const string Path = "/_test/throw";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(Path, branch => branch.Run(_ => throw new InvalidOperationException("boom-secret-detail")));
        };
    }
}
