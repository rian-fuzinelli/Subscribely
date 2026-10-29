using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Subscribely.Infrastructure.Tenancy;

namespace Subscribely.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "subscribely";

    public string Audience { get; set; } = "subscribely-api";

    /// <summary>Chave HMAC (≥ 32 bytes). Vem de variável de ambiente ou user-secrets, nunca do appsettings versionado.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    public SymmetricSecurityKey GetSigningKey()
    {
        var bytes = Encoding.UTF8.GetBytes(SigningKey);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must have at least 32 bytes.");
        }

        return new SymmetricSecurityKey(bytes);
    }
}

public static class Policies
{
    public const string PlatformAdmin = nameof(PlatformAdmin);
    public const string TenantAdmin = nameof(TenantAdmin);
    public const string TenantMember = nameof(TenantMember);
    public const string ApiKey = nameof(ApiKey);
}

public static class AuthenticationSetup
{
    /// <summary>Cookie usado só pelo dashboard do Hangfire (o navegador não envia o header Authorization).</summary>
    public const string DashboardCookie = "subscribely_dashboard";

    public static IServiceCollection AddSubscribelyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = jwt.Value.GetSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = SubscribelyClaims.Role,
                };
                bearer.Events = new JwtBearerEvents { OnMessageReceived = ReadDashboardToken };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.PlatformAdmin, p => p
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireRole(Roles.PlatformAdmin))
            .AddPolicy(Policies.TenantAdmin, p => p
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireRole(Roles.TenantAdmin)
                .RequireClaim(SubscribelyClaims.TenantId))
            .AddPolicy(Policies.TenantMember, p => p
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireRole(Roles.TenantAdmin, Roles.Member)
                .RequireClaim(SubscribelyClaims.TenantId));

        return services;
    }

    /// <summary>
    /// Para <c>/hangfire</c>: aceita <c>?access_token=</c> na primeira visita e o guarda num cookie HttpOnly
    /// restrito ao caminho do dashboard; as requisições seguintes (CSS/JS/polling) usam o cookie.
    /// </summary>
    private static Task ReadDashboardToken(MessageReceivedContext context)
    {
        var request = context.HttpContext.Request;
        if (!request.Path.StartsWithSegments("/hangfire", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var queryToken = request.Query["access_token"].ToString();
        if (!string.IsNullOrEmpty(queryToken))
        {
            context.Token = queryToken;
            context.HttpContext.Response.Cookies.Append(DashboardCookie, queryToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = "/hangfire",
            });
        }
        else if (request.Cookies.TryGetValue(DashboardCookie, out var cookieToken))
        {
            context.Token = cookieToken;
        }

        return Task.CompletedTask;
    }
}

public static class AuthorizationEndpointExtensions
{
    public static TBuilder RequirePlatformAdmin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.RequireAuthorization(Policies.PlatformAdmin);

    public static TBuilder RequireTenantAdmin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.RequireAuthorization(Policies.TenantAdmin);

    public static TBuilder RequireTenantMember<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.RequireAuthorization(Policies.TenantMember);
}
