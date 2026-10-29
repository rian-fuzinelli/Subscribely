using Subscribely.Infrastructure.Auditing;

namespace Subscribely.Modules.Projects.Domain;

/// <summary>Produto demo do data plane: existe para haver o que medir.</summary>
[Auditable]
internal sealed class Project
{
    public const int MaxNameLength = 100;

    private Project()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Project Create(string name, string? description, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name.Trim(),
        Description = description,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Update(string name, string? description, DateTimeOffset now)
    {
        Name = name.Trim();
        Description = description;
        UpdatedAt = now;
    }
}
