using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Subscribely.Infrastructure;
using Subscribely.Infrastructure.Jobs;
using Subscribely.Infrastructure.Outbox;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Payments.Contracts;
using Subscribely.Modules.Payments.Features.Charges;
using Subscribely.Modules.Payments.Features.Webhooks;
using Subscribely.Modules.Payments.Gateway;
using Subscribely.Modules.Payments.Persistence;

namespace Subscribely.Modules.Payments;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<PaymentsOptions>(configuration.GetSection(PaymentsOptions.Section));
        services.AddModuleDbContext<PaymentsDbContext>(PaymentsDbContext.SchemaName);
        services.AddScoped<IPaymentsApi, PaymentsApi>();
        services.AddScoped<ChargeService>();
        services.AddScoped<ProcessPaymentEventJob>();
        services.AddScoped<DunningJob>();
        services.AddScoped<FakeWebhookSender>();
        services.AddSingleton<FakeGatewayState>();
        services.AddScoped<IIntegrationEventHandler<InvoiceIssued>, OnInvoiceIssued>();
        services.AddSingleton<IRecurringJobsRegistrar, PaymentsRecurringJobs>();

        // Timeout total ≤ 10 s, retry com jitter e circuit breaker; o retry reenvia a mesma Idempotency-Key.
        services.AddHttpClient<IPaymentGateway, HttpPaymentGateway>((sp, http) =>
                http.BaseAddress = sp.GetRequiredService<IOptions<PaymentsOptions>>().Value.GatewayBase)
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            });
        services.AddHttpClient(FakeWebhookSender.HttpClientName).AddStandardResilienceHandler();
        return services;
    }

    public static IEndpointRouteBuilder MapPaymentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapWebhookEndpoints().MapChargeEndpoints();

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<PaymentsOptions>>().Value;
        if (string.Equals(options.Provider, PaymentsOptions.FakeProvider, StringComparison.OrdinalIgnoreCase))
        {
            // Replay só fora de produção: Development (demonstração) e Testing (suíte de integração).
            var environment = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();
            endpoints.MapFakeGatewayEndpoints(devEndpoints: environment.IsDevelopment() || environment.IsEnvironment("Testing"));
        }

        return endpoints;
    }
}

internal sealed class PaymentsApi(PaymentsDbContext db) : IPaymentsApi
{
    public Task<bool> HasDefaultPaymentMethodAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.PaymentMethods.AnyAsync(m => m.TenantId == tenantId, cancellationToken);
}
