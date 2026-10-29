using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.Infrastructure;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Audit.Contracts;
using Subscribely.Modules.Projects.Features;
using Subscribely.Modules.Projects.Persistence;

namespace Subscribely.Modules.Projects;

public static class ProjectsModule
{
    public static IServiceCollection AddProjectsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddTenantDbContext<ProjectsDbContext>();
        services.AddScoped<ITenantDatabaseMigrator, ProjectsDatabaseMigrator>();
        services.AddScoped<IProductAuditSource, ProductAuditSource>();
        services.AddHostedService<TenantDatabasesMigrationHostedService>();
        return services;
    }

    public static IEndpointRouteBuilder MapProjectsEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapProjectEndpoints();
}
