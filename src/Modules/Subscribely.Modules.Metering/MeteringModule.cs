using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.Infrastructure;
using Subscribely.Infrastructure.Jobs;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Metering.Features;
using Subscribely.Modules.Metering.Persistence;
using Subscribely.Modules.Metering.Quota;

namespace Subscribely.Modules.Metering;

public static class MeteringModule
{
    public static IServiceCollection AddMeteringModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MeteringOptions>(configuration.GetSection(MeteringOptions.Section));
        services.AddModuleDbContext<MeteringDbContext>(MeteringDbContext.SchemaName);
        services.AddSingleton<QuotaCounter>();
        services.AddScoped<QuotaAlerts>();
        services.AddScoped<IProductRequestGate, ProductRequestGate>();
        services.AddScoped<UsageSnapshotJob>();
        services.AddSingleton<IRecurringJobsRegistrar, MeteringRecurringJobs>();
        return services;
    }

    public static IEndpointRouteBuilder MapMeteringEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapUsageEndpoints();
}
