using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Subscribely.Infrastructure.Web;

/// <summary>
/// Gate dos endpoints de produto (status da assinatura e quota). Implementado pelo módulo Metering; o módulo
/// dono do produto só marca o grupo com <see cref="ProductEndpointExtensions.AsProductEndpoints"/>.
/// </summary>
public interface IProductRequestGate : IEndpointFilter;

public static class ProductEndpointExtensions
{
    /// <summary>Roda o <see cref="IProductRequestGate"/> registrado (se houver) antes de cada handler do grupo.</summary>
    public static RouteGroupBuilder AsProductEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.AddEndpointFilter(async (context, next) =>
            context.HttpContext.RequestServices.GetService<IProductRequestGate>() is { } gate
                ? await gate.InvokeAsync(context, next)
                : await next(context));
        return group;
    }
}
