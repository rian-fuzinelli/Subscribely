using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subscribely.SharedKernel;

namespace Subscribely.Infrastructure.Outbox;

/// <summary>Consumidor de um evento de integração. Precisa ser idempotente (entrega at-least-once).</summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    public bool DispatcherEnabled { get; set; } = true;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 50;
}

/// <summary>Processa um lote do outbox com <c>FOR UPDATE SKIP LOCKED</c> (seguro com várias instâncias).</summary>
public sealed partial class OutboxProcessor(
    OutboxDbContext db,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger)
{
    private static readonly System.Reflection.MethodInfo DispatchMethod =
        typeof(OutboxProcessor).GetMethod(nameof(DispatchTypedAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    /// <returns>Quantidade de mensagens lidas no lote.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var batchSize = options.Value.BatchSize;
        var messages = await db.Messages
            .FromSql($"""
                SELECT * FROM outbox.messages
                WHERE processed_at IS NULL AND attempts < {OutboxMessage.MaxAttempts}
                ORDER BY occurred_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await DispatchAsync(message, cancellationToken);
                message.MarkProcessed(timeProvider.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed(ex.Message);
                LogDispatchFailed(logger, ex, message.Id, message.Type, message.Attempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages.Count;
    }

    /// <summary>Processa até esvaziar (útil em testes e na inicialização).</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (await ProcessBatchAsync(cancellationToken) > 0)
        {
        }
    }

    private async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var @event = message.Deserialize();
        await using var scope = scopeFactory.CreateAsyncScope();
        var task = (Task)DispatchMethod.MakeGenericMethod(@event.GetType())
            .Invoke(null, [scope.ServiceProvider, @event, cancellationToken])!;
        await task;
    }

    private static async Task DispatchTypedAsync<TEvent>(IServiceProvider services, TEvent @event, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        foreach (var handler in services.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            await handler.HandleAsync(@event, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({Type}) failed on attempt {Attempts}")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception, Guid messageId, string type, int attempts);
}

/// <summary>Faz polling do outbox. Escolhido no lugar de um job recorrente do Hangfire (granularidade mínima de 1 min).</summary>
internal sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.DispatcherEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.PollingInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().DrainAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPollingFailed(logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox polling failed; retrying on next tick")]
    private static partial void LogPollingFailed(ILogger logger, Exception exception);
}
