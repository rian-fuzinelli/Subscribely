using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.Modules.Payments.Contracts;

namespace Subscribely.Modules.Payments;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<IPaymentsApi, PaymentsApi>();
        return services;
    }

    public static IEndpointRouteBuilder MapPaymentsEndpoints(this IEndpointRouteBuilder endpoints) => endpoints;
}

// Implementado de verdade na spec 05 (meios de pagamento). Até lá nenhum tenant tem meio de pagamento.
internal sealed class PaymentsApi : IPaymentsApi
{
    public Task<bool> HasDefaultPaymentMethodAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(false);
}
