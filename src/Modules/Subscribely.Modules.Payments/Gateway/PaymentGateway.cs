using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Subscribely.Modules.Payments.Gateway;

internal sealed class PaymentsOptions
{
    public const string Section = "Payments";

    public const string FakeProvider = "Fake";

    /// <summary><c>Fake</c> ou <c>None</c> (não cobra).</summary>
    public string Provider { get; set; } = FakeProvider;

    /// <summary>Base pública desta API, para onde o gateway envia os webhooks.</summary>
    public Uri WebhookBaseUrl { get; set; } = new("http://localhost:8080");

    /// <summary>Base do gateway; o Fake é servido por esta própria API.</summary>
    public Uri? GatewayBaseUrl { get; set; }

    public FakeGatewayOptions FakeGateway { get; set; } = new();

    public Uri GatewayBase => GatewayBaseUrl ?? WebhookBaseUrl;

    public bool ChargesEnabled => !string.Equals(Provider, "None", StringComparison.OrdinalIgnoreCase);
}

internal sealed class FakeGatewayOptions
{
    /// <summary>Segredo HMAC dos webhooks (env/user-secrets, nunca versionado).</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    public TimeSpan MinWebhookDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxWebhookDelay { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>Resposta do gateway ao criar uma charge (o resultado chega depois, por webhook).</summary>
internal sealed record GatewayCharge(string Id, string Status);

internal interface IPaymentGateway
{
    /// <summary>Mesma <paramref name="idempotencyKey"/> → mesma charge no gateway (retry seguro).</summary>
    Task<GatewayCharge> CreateChargeAsync(Guid invoiceId, long amountCents, string paymentMethodToken, string idempotencyKey, CancellationToken cancellationToken);
}

internal sealed record CreateChargeRequest(
    [property: JsonPropertyName("invoiceId")] Guid InvoiceId,
    [property: JsonPropertyName("amountCents")] long AmountCents,
    [property: JsonPropertyName("paymentMethod")] string PaymentMethod);

/// <summary>
/// Cliente HTTP do gateway (tipado, com o resilience handler padrão). O retry técnico reenvia a mesma
/// requisição, então o header <c>Idempotency-Key</c> é sempre o mesmo.
/// </summary>
internal sealed class HttpPaymentGateway(HttpClient http) : IPaymentGateway
{
    public const string ChargesPath = "fake-gateway/v1/charges";

    public async Task<GatewayCharge> CreateChargeAsync(
        Guid invoiceId, long amountCents, string paymentMethodToken, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ChargesPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new CreateChargeRequest(invoiceId, amountCents, paymentMethodToken)),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GatewayCharge>(cancellationToken)
            ?? throw new InvalidOperationException("Empty response from the payment gateway.");
    }
}
