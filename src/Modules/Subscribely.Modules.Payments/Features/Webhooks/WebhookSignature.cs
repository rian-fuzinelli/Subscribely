using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Subscribely.Modules.Payments.Features.Webhooks;

/// <summary>
/// Assinatura <c>X-Subscribely-Signature: t={unix},v1={hex}</c> com <c>v1 = HMAC-SHA256(secret, "{t}.{body}")</c>.
/// Comparação em tempo constante e tolerância de 5 min contra replay.
/// </summary>
internal static class WebhookSignature
{
    public const string HeaderName = "X-Subscribely-Signature";

    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static string Sign(string secret, string body, DateTimeOffset now)
    {
        var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return $"t={timestamp},v1={Convert.ToHexStringLower(Compute(secret, timestamp, body))}";
    }

    public static bool Verify(string secret, string? header, string body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(header))
        {
            return false;
        }

        string? timestamp = null;
        string? signature = null;
        foreach (var part in header.Split(','))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                return false;
            }

            switch (pair[0].Trim())
            {
                case "t":
                    timestamp = pair[1].Trim();
                    break;
                case "v1":
                    signature = pair[1].Trim();
                    break;
            }
        }

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) || signature is null)
        {
            return false;
        }

        var signedAt = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(unix, 0, 253_402_300_799));
        if ((now - signedAt).Duration() > Tolerance)
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(provided, Compute(secret, timestamp!, body));
    }

    private static byte[] Compute(string secret, string timestamp, string body) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
}
