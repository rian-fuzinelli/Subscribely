using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.Infrastructure;
using Subscribely.Infrastructure.Auditing;
using Subscribely.Modules.Audit.Features.QueryAudit;
using Subscribely.Modules.Audit.Persistence;

namespace Subscribely.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<AuditDbContext>(AuditEntryConfiguration.ControlSchema);
        services.AddScoped<IAuditWriter, AuditWriter>();
        return services;
    }

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapAuditQueryEndpoints();
}
