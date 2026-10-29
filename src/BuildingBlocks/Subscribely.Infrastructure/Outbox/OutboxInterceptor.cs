using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Subscribely.Infrastructure.Persistence;
using Subscribely.SharedKernel;

namespace Subscribely.Infrastructure.Outbox;

/// <summary>Converte os eventos pendentes em linhas do outbox dentro da mesma transação do SaveChanges.</summary>
internal sealed class OutboxInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        WriteOutbox(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        WriteOutbox(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void WriteOutbox(DbContext? context)
    {
        if (context is not ModuleDbContext moduleContext)
        {
            return;
        }

        var entities = moduleContext.ChangeTracker.Entries<Entity>()
            .Select(e => e.Entity)
            .Where(e => e.Events.Count > 0)
            .ToList();

        var events = entities.SelectMany(e => e.Events).Concat(moduleContext.PendingEvents).ToList();
        if (events.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        moduleContext.Set<OutboxMessage>().AddRange(events.Select(e => OutboxMessage.From(e, now)));

        entities.ForEach(e => e.ClearEvents());
        moduleContext.ClearPendingEvents();
    }
}
