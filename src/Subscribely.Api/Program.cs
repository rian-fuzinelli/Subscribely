using Subscribely.Modules.Audit;
using Subscribely.Modules.Billing;
using Subscribely.Modules.Metering;
using Subscribely.Modules.Notifications;
using Subscribely.Modules.Payments;
using Subscribely.Modules.Projects;
using Subscribely.Modules.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services
    .AddTenancyModule(builder.Configuration)
    .AddBillingModule(builder.Configuration)
    .AddPaymentsModule(builder.Configuration)
    .AddMeteringModule(builder.Configuration)
    .AddNotificationsModule(builder.Configuration)
    .AddAuditModule(builder.Configuration)
    .AddProjectsModule(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapTenancyEndpoints()
    .MapBillingEndpoints()
    .MapPaymentsEndpoints()
    .MapMeteringEndpoints()
    .MapNotificationsEndpoints()
    .MapAuditEndpoints()
    .MapProjectsEndpoints();

app.Run();

public partial class Program;
