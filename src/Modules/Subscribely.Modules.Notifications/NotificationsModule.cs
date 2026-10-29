using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subscribely.Infrastructure;
using Subscribely.Infrastructure.Outbox;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Metering.Contracts;
using Subscribely.Modules.Notifications.Email;
using Subscribely.Modules.Notifications.Features;
using Subscribely.Modules.Notifications.Persistence;
using Subscribely.Modules.Payments.Contracts;
using Subscribely.Modules.Tenancy.Contracts;

namespace Subscribely.Modules.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.Section));
        services.Configure<NotificationsOptions>(configuration.GetSection(NotificationsOptions.Section));
        services.AddModuleDbContext<NotificationsDbContext>(NotificationsDbContext.SchemaName);
        services.AddSingleton<TemplateRenderer>();
        services.AddSingleton<IEmailSender, MailKitEmailSender>();
        services.AddScoped<EmailDispatcher>();
        services.AddScoped<EmailContext>();

        services.AddScoped<IIntegrationEventHandler<TrialEnding>, OnTrialEnding>();
        services.AddScoped<IIntegrationEventHandler<InvoiceIssued>, OnInvoiceIssued>();
        services.AddScoped<IIntegrationEventHandler<InvoicePaid>, OnInvoicePaid>();
        services.AddScoped<IIntegrationEventHandler<PaymentFailed>, OnPaymentFailed>();
        services.AddScoped<IIntegrationEventHandler<SubscriptionSuspended>, OnSubscriptionSuspended>();
        services.AddScoped<IIntegrationEventHandler<QuotaThresholdReached>, OnQuotaThresholdReached>();
        services.AddScoped<IIntegrationEventHandler<UserInvited>, OnUserInvited>();
        services.AddScoped<IIntegrationEventHandler<PasswordResetRequested>, OnPasswordResetRequested>();
        return services;
    }

    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapNotificationAdminEndpoints();
}
