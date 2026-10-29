using Scalar.AspNetCore;
using Subscribely.Infrastructure;
using Subscribely.Infrastructure.Jobs;
using Subscribely.Modules.Audit;
using Subscribely.Modules.Billing;
using Subscribely.Modules.Metering;
using Subscribely.Modules.Notifications;
using Subscribely.Modules.Payments;
using Subscribely.Modules.Projects;
using Subscribely.Modules.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.UseUtcTimestamp = true;
    o.TimestampFormat = "O";
});

builder.Services.AddSubscribelyInfrastructure(builder.Configuration);
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
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

app.UseSubscribelyInfrastructure();
app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapSubscribelyHealthChecks();
app.MapSubscribelyJobsDashboard();

app.MapTenancyEndpoints()
    .MapBillingEndpoints()
    .MapPaymentsEndpoints()
    .MapMeteringEndpoints()
    .MapNotificationsEndpoints()
    .MapAuditEndpoints()
    .MapProjectsEndpoints();

await app.RunAsync();

public partial class Program;
