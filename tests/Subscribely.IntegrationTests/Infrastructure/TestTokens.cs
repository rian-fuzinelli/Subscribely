using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Subscribely.Infrastructure.Tenancy;

namespace Subscribely.IntegrationTests.Infrastructure;

/// <summary>Emite JWTs válidos para a chave de teste, sem passar pelo login (para testar autorização isoladamente).</summary>
public static class TestTokens
{
    public const string SigningKey = "integration-tests-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz";

    public static string Issue(string role, Guid? tenantId = null, Guid? userId = null)
    {
        var claims = new List<Claim>
        {
            new("sub", (userId ?? Guid.NewGuid()).ToString()),
            new(SubscribelyClaims.Role, role),
        };
        if (tenantId is not null)
        {
            claims.Add(new Claim(SubscribelyClaims.TenantId, tenantId.Value.ToString()));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "subscribely",
            Audience = "subscribely-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256),
        });
    }

    public static HttpClient WithBearer(this HttpClient client, string token)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static HttpClient AsPlatformAdmin(this HttpClient client) => client.WithBearer(Issue(Roles.PlatformAdmin));
}
