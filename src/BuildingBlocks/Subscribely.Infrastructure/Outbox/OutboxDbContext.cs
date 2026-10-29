using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Persistence;

namespace Subscribely.Infrastructure.Outbox;

/// <summary>Dono das migrations da tabela <c>outbox.messages</c> e usado pelo processador.</summary>
public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "outbox";

    public override string Schema => SchemaName;

    public DbSet<OutboxMessage> Messages => Set<OutboxMessage>();
}
