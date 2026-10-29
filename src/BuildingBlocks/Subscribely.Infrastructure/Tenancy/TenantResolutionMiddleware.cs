using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Subscribely.Infrastructure.Tenancy;

/// <summary>Preenche o <see cref="TenantContext"/> a partir do principal autenticado (JWT ou API key).</summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var tenantId = Guid.TryParse(user.FindFirstValue(SubscribelyClaims.TenantId), out var tid) ? tid : (Guid?)null;
            var apiKeyId = user.FindFirstValue(SubscribelyClaims.ApiKeyId);
            var actorType = apiKeyId is null ? ActorType.User : ActorType.ApiKey;
            var actorId = apiKeyId ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

            tenantContext.Set(
                tenantId,
                user.FindFirstValue(SubscribelyClaims.TenantSlug),
                actorType,
                actorId,
                user.FindFirstValue(SubscribelyClaims.Role) ?? user.FindFirstValue(ClaimTypes.Role));
        }

        return next(context);
    }
}
