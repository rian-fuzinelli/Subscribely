using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Subscribely.Infrastructure;
using Subscribely.Infrastructure.Outbox;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Tenancy.Contracts;
using Subscribely.Modules.Tenancy.Domain;
using Subscribely.Modules.Tenancy.Features.ApiKeys;
using Subscribely.Modules.Tenancy.Features.Auth;
using Subscribely.Modules.Tenancy.Features.Signup;
using Subscribely.Modules.Tenancy.Features.Tenants;
using Subscribely.Modules.Tenancy.Features.Users;
using Subscribely.Modules.Tenancy.Persistence;
using Subscribely.Modules.Tenancy.Security;

namespace Subscribely.Modules.Tenancy;

public static class TenancyModule
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<TenancyDbContext>(TenancyDbContext.SchemaName);
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, null);

        services.AddSingleton<Passwords>();
        services.AddScoped<TokenService>();
        services.AddScoped<ApiKeyCache>();
        services.AddScoped<SeatPolicy>();
        services.AddScoped<TenantDatabaseProvisioner>();
        services.AddScoped<ProvisionTenantJob>();
        services.AddScoped<ITenancyApi, TenancyApi>();
        services.AddScoped<IIntegrationEventHandler<TenantSignedUp>, OnTenantSignedUp>();
        services.AddHostedService<PlatformAdminSeed>();
        return services;
    }

    public static IEndpointRouteBuilder MapTenancyEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints
            .MapSignupEndpoints()
            .MapAuthEndpoints()
            .MapUserEndpoints()
            .MapApiKeyEndpoints()
            .MapTenantEndpoints();
}

/// <summary>Cria o PlatformAdmin de desenvolvimento (idempotente). Senha em <c>Seed:PlatformAdminPassword</c>.</summary>
internal sealed class PlatformAdminSeed(IServiceProvider services, IHostEnvironment environment, IConfiguration configuration, TimeProvider time) : IHostedService
{
    public const string Email = "admin@subscribely.local";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var password = configuration["Seed:PlatformAdminPassword"];
        if (!environment.IsDevelopment() || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        if (await db.Users.AnyAsync(u => u.Email == Email, cancellationToken))
        {
            return;
        }

        var passwords = scope.ServiceProvider.GetRequiredService<Passwords>();
        db.Users.Add(User.CreateActive(null, Email, Roles.PlatformAdmin, u => passwords.Hash(u, password), time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
