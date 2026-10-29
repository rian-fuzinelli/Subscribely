using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.Infrastructure;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Features.Plans;
using Subscribely.Modules.Billing.Persistence;

namespace Subscribely.Modules.Billing;

public static class BillingModule
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<BillingDbContext>(BillingDbContext.SchemaName);
        services.AddScoped<IBillingApi, BillingApi>();
        services.AddHostedService<DevelopmentSeed>();
        return services;
    }

    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPlanEndpoints();
}
