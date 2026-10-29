using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Subscribely.Infrastructure.Persistence;

/// <summary>
/// Chaves Guid são geradas na aplicação (<c>Guid.CreateVersion7()</c>). Sem esta convenção o EF trata
/// uma entidade nova alcançada por navegação (com Id já preenchido) como existente e emite UPDATE.
/// </summary>
internal sealed class ClientGeneratedKeysConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var property in entityType.FindPrimaryKey()?.Properties ?? [])
            {
                if (property.ClrType == typeof(Guid))
                {
                    property.Builder.ValueGenerated(ValueGenerated.Never);
                }
            }
        }
    }
}
