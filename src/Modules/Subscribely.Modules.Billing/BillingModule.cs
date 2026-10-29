using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.Infrastructure;
using Subscribely.Infrastructure.Jobs;
using Subscribely.Infrastructure.Outbox;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Billing.Features.Invoices;
using Subscribely.Modules.Billing.Features.Plans;
using Subscribely.Modules.Billing.Features.Subscriptions;
using Subscribely.Modules.Billing.Features.Subscriptions.Jobs;
using Subscribely.Modules.Billing.Persistence;
using Subscribely.Modules.Payments.Contracts;
using Subscribely.Modules.Tenancy.Contracts;

namespace Subscribely.Modules.Billing;

public static class BillingModule
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<BillingDbContext>(BillingDbContext.SchemaName);
        services.AddScoped<IBillingApi, BillingApi>();
        services.AddScoped<EntitlementsCache>();
        services.AddScoped<SubscriptionStore>();
        services.AddScoped<InvoiceIssuer>();
        services.AddHostedService<DevelopmentSeed>();

        services.AddScoped<IIntegrationEventHandler<TenantProvisioned>, OnTenantProvisioned>();
        services.AddScoped<IIntegrationEventHandler<InvoicePaid>, OnInvoicePaid>();
        services.AddScoped<IIntegrationEventHandler<PaymentFailed>, OnPaymentFailed>();
        services.AddScoped<IIntegrationEventHandler<PaymentRetriesExhausted>, OnPaymentRetriesExhausted>();

        services.AddScoped<SubscriptionBatchRunner>();
        services.AddScoped<TrialEndingNotifierJob>();
        services.AddScoped<TrialExpiryJob>();
        services.AddScoped<PeriodEndJob>();
        services.AddScoped<SuspensionExpiryJob>();
        services.AddSingleton<IRecurringJobsRegistrar, BillingRecurringJobs>();
        return services;
    }

    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPlanEndpoints().MapSubscriptionEndpoints().MapInvoiceEndpoints();
}
