using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Tenancy.Domain;
using Subscribely.Modules.Tenancy.Persistence;
using Subscribely.Modules.Tenancy.Security;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Tenancy.Features.ApiKeys;

internal sealed class CreateApiKeyRequest
{
    [Required]
    [StringLength(60, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;
}

internal sealed record CreatedApiKeyResponse(Guid Id, string Name, string Key, DateTimeOffset CreatedAt);

internal sealed record ApiKeyResponse(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

internal static class ApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var keys = endpoints.MapGroup("/v1/api-keys").WithTags("API keys").RequireTenantAdmin();
        keys.MapPost("/", CreateAsync).WithRequestValidation<CreateApiKeyRequest>();
        keys.MapGet("/", ListAsync);
        keys.MapDelete("/{id:guid}", RevokeAsync);
        return endpoints;
    }

    /// <summary>O segredo completo aparece só nesta resposta.</summary>
    private static async Task<IResult> CreateAsync(
        CreateApiKeyRequest request, ITenantContext tenant, TenancyDbContext db, TimeProvider time, CancellationToken ct)
    {
        var createdBy = Guid.Parse(tenant.ActorId!);
        var (key, plainText) = ApiKey.Create(tenant.RequiredTenantId, request.Name, createdBy, time.GetUtcNow());
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/v1/api-keys/{key.Id}", new CreatedApiKeyResponse(key.Id, key.Name, plainText, key.CreatedAt));
    }

    private static async Task<IResult> ListAsync(TenancyDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.ApiKeys.AsNoTracking().OrderBy(k => k.CreatedAt)
            .Select(k => new ApiKeyResponse(k.Id, k.Name, ApiKey.Prefix + k.KeyPrefix, k.CreatedAt, k.RevokedAt))
            .ToListAsync(ct));

    private static async Task<IResult> RevokeAsync(Guid id, TenancyDbContext db, ApiKeyCache cache, TimeProvider time, CancellationToken ct)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null)
        {
            return Error.NotFound().ToProblem();
        }

        key.Revoke(time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await cache.InvalidateAsync(key.KeyPrefix, ct);
        return TypedResults.NoContent();
    }
}
