using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Subscribely.Modules.Metering;

public static class MeteringModule
{
    public static IServiceCollection AddMeteringModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapMeteringEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}
