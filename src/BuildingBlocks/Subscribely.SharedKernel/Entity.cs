namespace Subscribely.SharedKernel;

/// <summary>Evento publicado via outbox e consumido de forma assíncrona (at-least-once) por outros módulos.</summary>
public interface IIntegrationEvent;

public abstract class Entity
{
    private readonly List<IIntegrationEvent> _events = [];

    protected Entity(Guid id) => Id = id;

    protected Entity() => Id = Guid.CreateVersion7();

    public Guid Id { get; private set; }

    public IReadOnlyList<IIntegrationEvent> Events => _events;

    protected void Raise(IIntegrationEvent @event) => _events.Add(@event);

    public void ClearEvents() => _events.Clear();
}
