using System.ComponentModel.DataAnnotations;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Subscribely.Infrastructure;
using Subscribely.Infrastructure.Jobs;
using Subscribely.Infrastructure.Outbox;
using Subscribely.Infrastructure.Persistence;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Billing.Contracts;
using Subscribely.Modules.Tenancy.Domain;
using Subscribely.Modules.Tenancy.Persistence;
using Subscribely.Modules.Tenancy.Security;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Tenancy.Features.Signup;

internal sealed class SignupRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string CompanyName { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^[a-z0-9-]{3,40}$", ErrorMessage = "Slug must have 3 to 40 lowercase letters, digits or hyphens.")]
    public string Slug { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string AdminEmail { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;

    [Required]
    public string PlanCode { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^(monthly|annual)$", ErrorMessage = "Interval must be 'monthly' or 'annual'.")]
    public string Interval { get; init; } = string.Empty;
}

internal sealed record SignupResponse(Guid TenantId, string Status);

internal static class SignupEndpoints
{
    public static IEndpointRouteBuilder MapSignupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/signup", SignupAsync)
            .WithTags("Signup")
            .AllowAnonymous()
            .WithRequestValidation<SignupRequest>()
            .Produces<SignupResponse>(StatusCodes.Status202Accepted);

        // Acompanhamento do provisionamento pelo portal. O id (UUID v7) só é conhecido por quem fez o signup.
        endpoints.MapGet("/v1/signup/{tenantId:guid}", GetStatusAsync)
            .WithTags("Signup")
            .AllowAnonymous()
            .Produces<SignupResponse>();
        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(Guid tenantId, TenancyDbContext db, CancellationToken ct)
    {
        var status = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (TenantStatus?)t.Status)
            .FirstOrDefaultAsync(ct);
        return status is { } value
            ? TypedResults.Ok(new SignupResponse(tenantId, value.ToString()))
            : Error.NotFound("signup-not-found", "Signup not found.").ToProblem();
    }

    private static async Task<IResult> SignupAsync(
        SignupRequest request,
        TenancyDbContext db,
        IBillingApi billing,
        TenantDatabaseFactory databases,
        Passwords passwords,
        TimeProvider time,
        CancellationToken ct)
    {
        if (await billing.GetActivePlanVersionAsync(request.PlanCode, ct) is null)
        {
            return Error.Unprocessable("plan-unavailable", $"Plan '{request.PlanCode}' is not available.").ToProblem();
        }

        var email = User.NormalizeEmail(request.AdminEmail);
        if (await db.Tenants.AnyAsync(t => t.Slug == request.Slug, ct))
        {
            return SlugTaken();
        }

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return EmailUnavailable();
        }

        var now = time.GetUtcNow();
        var tenant = Tenant.SignUp(request.Slug, request.CompanyName, databases.DatabaseNameFor(request.Slug), request.PlanCode, request.Interval, now);
        db.Tenants.Add(tenant);
        db.Users.Add(User.CreateActive(tenant.Id, email, Roles.TenantAdmin, u => passwords.Hash(u, request.Password), now));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_tenants_slug"))
        {
            return SlugTaken();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_users_email"))
        {
            return EmailUnavailable();
        }

        return TypedResults.Accepted((string?)null, new SignupResponse(tenant.Id, tenant.Status.ToString()));
    }

    private static IResult SlugTaken() =>
        (Error.Conflict("slug-taken", "This slug is already in use.") with { Details = new Dictionary<string, object?> { ["field"] = "slug" } }).ToProblem();

    // Não revela a qual tenant o e-mail pertence.
    private static IResult EmailUnavailable() =>
        (Error.Conflict("email-unavailable", "This e-mail cannot be used.") with { Details = new Dictionary<string, object?> { ["field"] = "adminEmail" } }).ToProblem();
}

/// <summary>Consumidor do outbox: agenda o provisionamento (o Hangfire cuida das retentativas).</summary>
internal sealed class OnTenantSignedUp(IBackgroundJobClient jobs) : IIntegrationEventHandler<TenantSignedUp>
{
    public Task HandleAsync(TenantSignedUp @event, CancellationToken cancellationToken)
    {
        jobs.Enqueue<ProvisionTenantJob>(job => job.RunAsync(@event.TenantId, CancellationToken.None));
        return Task.CompletedTask;
    }
}

/// <summary>Cria o banco do tenant (idempotente), aplica as migrations e ativa o tenant.</summary>
internal sealed partial class ProvisionTenantJob(
    TenancyDbContext db,
    TenantDatabaseProvisioner provisioner,
    TenantContext tenantContext,
    ILogger<ProvisionTenantJob> logger)
{
    [Queue(JobQueues.Critical)]
    [AutomaticRetry(Attempts = Tenant.MaxProvisioningAttempts - 1, DelaysInSeconds = [10, 60], OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        tenantContext.SetSystem(nameof(ProvisionTenantJob), tenantId);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Provisioning)
        {
            return;
        }

        try
        {
            await provisioner.ProvisionAsync(tenant.Slug, cancellationToken);
            tenant.MarkProvisioned();
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var exhausted = tenant.RegisterProvisioningFailure();
            await db.SaveChangesAsync(CancellationToken.None);
            LogProvisioningFailed(logger, ex, tenantId, tenant.ProvisioningAttempts);
            if (!exhausted)
            {
                throw;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Provisioning of tenant {TenantId} failed (attempt {Attempt})")]
    private static partial void LogProvisioningFailed(ILogger logger, Exception exception, Guid tenantId, int attempt);
}

internal sealed class TenantDatabaseProvisioner(
    IConfiguration configuration,
    TenantDatabaseFactory databases,
    IOptions<TenantDatabaseOptions> options,
    IServiceProvider services)
{
    public async Task ProvisionAsync(string slug, CancellationToken ct)
    {
        var databaseName = databases.DatabaseNameFor(slug);
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("PostgresAdmin"));
        await connection.OpenAsync(ct);

        await using (var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection))
        {
            exists.Parameters.AddWithValue("name", databaseName);
            if (await exists.ExecuteScalarAsync(ct) is null)
            {
                await CreateDatabaseAsync(connection, databaseName, ct);
            }
        }

        if (services.GetService<ITenantDatabaseMigrator>() is { } migrator)
        {
            await migrator.MigrateAsync(slug, ct);
        }
    }

    private async Task CreateDatabaseAsync(NpgsqlConnection connection, string databaseName, CancellationToken ct)
    {
        var quote = new NpgsqlCommandBuilder();
        var owner = options.Value.Owner;
        var sql = $"CREATE DATABASE {quote.QuoteIdentifier(databaseName)}"
            + (string.IsNullOrWhiteSpace(owner) ? string.Empty : $" OWNER {quote.QuoteIdentifier(owner)}");
        try
        {
#pragma warning disable CA2100 // Identificadores validados (regex do slug) e escapados; CREATE DATABASE não aceita parâmetros.
            await using var create = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
            await create.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateDatabase)
        {
            // Outra execução criou o banco ao mesmo tempo: segue para as migrations.
        }
    }
}
