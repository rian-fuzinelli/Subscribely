using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.Infrastructure.Outbox;
using Subscribely.Modules.Payments.Contracts;
using Subscribely.SharedKernel;

namespace Subscribely.IntegrationTests.Infrastructure;

public static class FactoryExtensions
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Executa um job (método <c>RunAsync(CancellationToken)</c> que devolve a quantidade alterada) num escopo novo.</summary>
    public static async Task<int> RunJobAsync<TJob>(this SubscribelyApiFactory factory)
        where TJob : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<TJob>();
        return await (Task<int>)typeof(TJob).GetMethod("RunAsync")!.Invoke(job, [Ct])!;
    }

    /// <summary>Entrega um evento direto aos consumidores (como o outbox faria).</summary>
    public static async Task HandleAsync<TEvent>(this SubscribelyApiFactory factory, TEvent @event)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        foreach (var handler in scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            await handler.HandleAsync(@event, Ct);
        }
    }

    public static Task<int> CountOutboxAsync(this SubscribelyApiFactory factory, string eventType, string containing)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var typePattern = $"%.{eventType},%";
        var pattern = $"%{containing}%";
        return factory.WithDbAsync<OutboxDbContext, int>(db => db.Messages
            .FromSql($"SELECT * FROM outbox.messages WHERE type LIKE {typePattern} AND payload::text LIKE {pattern}")
            .CountAsync(Ct));
    }
}

/// <summary>Meio de pagamento simulado por tenant (substitui o <see cref="IPaymentsApi"/> nos testes de Billing).</summary>
public sealed class FakePaymentsApi : IPaymentsApi
{
    public static ConcurrentDictionary<Guid, bool> WithPaymentMethod { get; } = new();

    public Task<bool> HasDefaultPaymentMethodAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(WithPaymentMethod.ContainsKey(tenantId));
}
