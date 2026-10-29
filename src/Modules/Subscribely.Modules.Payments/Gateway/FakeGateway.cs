using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Subscribely.Infrastructure.Jobs;
using Subscribely.Modules.Payments.Features.Webhooks;

namespace Subscribely.Modules.Payments.Gateway;

/// <summary>Corpo do webhook, igual ao de um gateway real.</summary>
internal sealed record WebhookPayload(string Id, string Type, DateTimeOffset CreatedAt, WebhookChargeData Data)
{
    public const string Succeeded = "charge.succeeded";
    public const string Failed = "charge.failed";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record WebhookChargeData(string ChargeId, Guid InvoiceId, long AmountCents, string? FailureCode);

/// <summary>Estado em memória do gateway falso: charges por chave de idempotência e webhooks enviados (para o replay).</summary>
internal sealed class FakeGatewayState
{
    public ConcurrentDictionary<string, GatewayCharge> Charges { get; } = new();

    public ConcurrentDictionary<Guid, int> AttemptsByInvoice { get; } = new();

    public ConcurrentDictionary<string, string> SentEvents { get; } = new();
}

/// <summary>
/// Gateway de pagamento falso servido pela própria API: decide o resultado pelo token de teste e agenda
/// (1–3 s) o envio HTTP do webhook assinado. Mesma chave de idempotência → mesma charge, sem novo webhook.
/// </summary>
internal static class FakeGatewayEndpoints
{
    public const string ProviderName = "fake";

    public static IEndpointRouteBuilder MapFakeGatewayEndpoints(this IEndpointRouteBuilder endpoints, bool devEndpoints)
    {
        endpoints.MapPost("/" + HttpPaymentGateway.ChargesPath, CreateChargeAsync).ExcludeFromDescription().AllowAnonymous();
        if (devEndpoints)
        {
            endpoints.MapPost("/dev/fake-gateway/events/{eventId}/replay", Replay).WithTags("Dev").AllowAnonymous();
        }

        return endpoints;
    }

    internal static (bool Success, string? FailureCode) Decide(string token, int attemptForInvoice) => token switch
    {
        "pm_card_ok" => (true, null),
        "pm_card_declined" => (false, "card_declined"),
        "pm_card_insufficient_funds" => (false, "insufficient_funds"),
        "pm_card_fail_then_ok" => attemptForInvoice == 1 ? (false, "card_declined") : (true, null),
        _ => (false, "invalid_payment_method"),
    };

    private static IResult CreateChargeAsync(
        HttpRequest request,
        CreateChargeRequest body,
        FakeGatewayState state,
        IBackgroundJobClient jobs,
        IOptions<PaymentsOptions> options,
        TimeProvider time)
    {
        var key = request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            return TypedResults.BadRequest(new { error = "idempotency_key_required" });
        }

        lock (state)
        {
            if (state.Charges.TryGetValue(key, out var existing))
            {
                return TypedResults.Ok(existing);
            }

            var attempt = state.AttemptsByInvoice.AddOrUpdate(body.InvoiceId, 1, (_, n) => n + 1);
            var (success, failureCode) = Decide(body.PaymentMethod, attempt);
            var charge = new GatewayCharge("ch_" + Guid.NewGuid().ToString("N"), "pending");
            state.Charges[key] = charge;

            var payload = new WebhookPayload(
                "evt_" + Guid.NewGuid().ToString("N"),
                success ? WebhookPayload.Succeeded : WebhookPayload.Failed,
                time.GetUtcNow(),
                new WebhookChargeData(charge.Id, body.InvoiceId, body.AmountCents, failureCode));
            var json = JsonSerializer.Serialize(payload, WebhookPayload.Json);
            state.SentEvents[payload.Id] = json;

            var fake = options.Value.FakeGateway;
            var spread = (int)Math.Max(0, (fake.MaxWebhookDelay - fake.MinWebhookDelay).TotalMilliseconds);
            var delay = fake.MinWebhookDelay + TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(spread + 1));
            jobs.Schedule<FakeWebhookSender>(s => s.SendAsync(json, CancellationToken.None), delay);
            return TypedResults.Ok(charge);
        }
    }

    private static IResult Replay(string eventId, FakeGatewayState state, IBackgroundJobClient jobs)
    {
        if (!state.SentEvents.TryGetValue(eventId, out var json))
        {
            return TypedResults.NotFound();
        }

        jobs.Enqueue<FakeWebhookSender>(s => s.SendAsync(json, CancellationToken.None));
        return TypedResults.Ok(new { replayed = eventId });
    }
}

/// <summary>Envia o webhook por HTTP real, assinado com HMAC (o Hangfire reenvia em caso de falha).</summary>
internal sealed class FakeWebhookSender(IHttpClientFactory httpClients, IOptions<PaymentsOptions> options, TimeProvider time)
{
    public const string HttpClientName = "fake-gateway-webhooks";

    [Queue(JobQueues.Critical)]
    [AutomaticRetry(Attempts = 5)]
    public async Task SendAsync(string json, CancellationToken cancellationToken)
    {
        var http = httpClients.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.Value.WebhookBaseUrl, $"webhooks/payments/{FakeGatewayEndpoints.ProviderName}"))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(WebhookSignature.HeaderName, WebhookSignature.Sign(options.Value.FakeGateway.WebhookSecret, json, time.GetUtcNow()));

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
