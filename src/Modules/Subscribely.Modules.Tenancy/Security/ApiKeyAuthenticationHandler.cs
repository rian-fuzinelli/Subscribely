using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Modules.Tenancy.Domain;
using Subscribely.Modules.Tenancy.Persistence;

namespace Subscribely.Modules.Tenancy.Security;

/// <summary>Dados mínimos de uma API key, cacheados por 60 s (memória + Redis).</summary>
internal sealed record CachedApiKey(Guid KeyId, Guid TenantId, string TenantSlug, string KeyHash, bool Usable);

internal sealed class ApiKeyCache(HybridCache cache, TenancyDbContext db)
{
    public static readonly HybridCacheEntryOptions Options = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(60),
    };

    public static string Key(string prefix) => $"apikey:{prefix}";

    public ValueTask<CachedApiKey?> GetAsync(string prefix, CancellationToken ct) =>
        cache.GetOrCreateAsync(Key(prefix), async token =>
        {
            var row = await (from k in db.ApiKeys.IgnoreQueryFilters()
                             join t in db.Tenants.IgnoreQueryFilters() on k.TenantId equals t.Id
                             where k.KeyPrefix == prefix
                             select new { k.Id, k.TenantId, t.Slug, k.KeyHash, k.RevokedAt, t.Status })
                .FirstOrDefaultAsync(token);
            return row is null
                ? null
                : new CachedApiKey(row.Id, row.TenantId, row.Slug, row.KeyHash, row.RevokedAt == null && row.Status == TenantStatus.Active);
        }, Options, cancellationToken: ct);

    public ValueTask InvalidateAsync(string prefix, CancellationToken ct) => cache.RemoveAsync(Key(prefix), ct);
}

/// <summary>Autentica o header <c>X-Api-Key</c> (nunca query string) e emite um principal com tid/kid.</summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyCache cache)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var header))
        {
            return AuthenticateResult.NoResult();
        }

        if (!ApiKey.TryParse(header.ToString(), out var prefix, out var secret))
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var key = await cache.GetAsync(prefix, Context.RequestAborted);
        if (key is not { Usable: true } || !Secrets.HashEquals(secret, key.KeyHash))
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(SubscribelyClaims.ApiKeyId, key.KeyId.ToString()),
                new Claim(SubscribelyClaims.TenantId, key.TenantId.ToString()),
                new Claim(SubscribelyClaims.TenantSlug, key.TenantSlug),
            ],
            ApiKeyDefaults.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyDefaults.Scheme));
    }
}
