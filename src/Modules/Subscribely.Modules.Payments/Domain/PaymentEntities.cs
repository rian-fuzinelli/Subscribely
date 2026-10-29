using Subscribely.Infrastructure.Auditing;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Payments.Domain;

/// <summary>Meio de pagamento padrão do tenant: só o token do gateway e dados de exibição (nunca o cartão).</summary>
[Auditable]
internal sealed class PaymentMethod
{
    private PaymentMethod()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    [NotAudited]
    public string GatewayToken { get; private set; } = string.Empty;

    public string Brand { get; private set; } = string.Empty;

    public string Last4 { get; private set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PaymentMethod Create(Guid tenantId, string token, DateTimeOffset now)
    {
        var method = new PaymentMethod { Id = Guid.CreateVersion7(), TenantId = tenantId };
        method.Replace(token, now);
        return method;
    }

    public void Replace(string token, DateTimeOffset now)
    {
        GatewayToken = token;
        (Brand, Last4) = Describe(token);
        UpdatedAt = now;
    }

    // Bandeira e final simulados a partir do token de teste (o Fake não tem cartão de verdade).
    private static (string Brand, string Last4) Describe(string token) => token switch
    {
        "pm_card_ok" => ("visa", "4242"),
        "pm_card_declined" => ("visa", "0002"),
        "pm_card_insufficient_funds" => ("mastercard", "9995"),
        "pm_card_fail_then_ok" => ("mastercard", "3184"),
        _ => ("unknown", "0000"),
    };
}

internal enum ChargeStatus
{
    Pending,
    Succeeded,
    Failed,
}

/// <summary>Uma tentativa de cobrança de uma fatura. Chave de idempotência = <c>{invoiceId}:{attempt}</c>.</summary>
[Auditable]
internal sealed class Charge
{
    public const string GatewayUnavailable = "gateway_unavailable";

    private Charge()
    {
    }

    public Guid Id { get; private set; }

    public Guid InvoiceId { get; private set; }

    public Guid TenantId { get; private set; }

    public int Attempt { get; private set; }

    /// <summary>Tentativa pedida pelo tenant (pagar agora / novo meio de pagamento), fora da agenda do dunning.</summary>
    public bool Manual { get; private set; }

    public string? GatewayChargeId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public long AmountCents { get; private set; }

    public ChargeStatus Status { get; private set; }

    public string? FailureCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsTerminal => Status != ChargeStatus.Pending;

    public static string KeyFor(Guid invoiceId, int attempt) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{invoiceId}:{attempt}");

    public static Charge Start(Guid invoiceId, Guid tenantId, int attempt, bool manual, long amountCents, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        InvoiceId = invoiceId,
        TenantId = tenantId,
        Attempt = attempt,
        Manual = manual,
        IdempotencyKey = KeyFor(invoiceId, attempt),
        AmountCents = amountCents,
        Status = ChargeStatus.Pending,
        CreatedAt = now,
    };

    public void AttachGatewayCharge(string gatewayChargeId) => GatewayChargeId = gatewayChargeId;

    public void Succeed(DateTimeOffset now)
    {
        Status = ChargeStatus.Succeeded;
        CompletedAt = now;
    }

    public void Fail(string failureCode, DateTimeOffset now)
    {
        Status = ChargeStatus.Failed;
        FailureCode = failureCode;
        CompletedAt = now;
    }
}

internal enum PaymentEventStatus
{
    Received,
    Processed,
    Ignored,
    Rejected,
}

/// <summary>Webhook recebido, gravado antes de qualquer processamento (insert-first).</summary>
internal sealed class PaymentEvent
{
    private PaymentEvent()
    {
    }

    public Guid Id { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string ProviderEventId { get; private set; } = string.Empty;

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public PaymentEventStatus Status { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public static PaymentEvent Receive(string provider, string providerEventId, string type, string payload, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        Provider = provider,
        ProviderEventId = providerEventId,
        Type = type,
        Payload = payload,
        Status = PaymentEventStatus.Received,
        ReceivedAt = now,
    };

    public void Complete(PaymentEventStatus status, DateTimeOffset now, string? note = null)
    {
        Status = status;
        Note = note;
        ProcessedAt = now;
    }
}

/// <summary>
/// Estado do dunning de uma fatura. Retentativas agendadas em D+1, D+3 e D+7 a partir da emissão;
/// a falha da última esgota as tentativas.
/// </summary>
[Auditable]
internal sealed class Dunning : Entity
{
    public static readonly TimeSpan[] RetryOffsets = [TimeSpan.FromDays(1), TimeSpan.FromDays(3), TimeSpan.FromDays(7)];

    private Dunning()
    {
    }

    public Guid InvoiceId { get; private set; }

    public Guid TenantId { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>Tentativas agendadas que falharam (a cobrança inicial conta como a primeira).</summary>
    public int Attempts { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? ExhaustedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsOpen => ExhaustedAt is null && ResolvedAt is null;

    public static Dunning Open(Guid invoiceId, Guid tenantId, DateTimeOffset issuedAt) => new()
    {
        InvoiceId = invoiceId,
        TenantId = tenantId,
        IssuedAt = issuedAt,
    };

    /// <summary>Próxima tentativa após <paramref name="failedAttempts"/> falhas; <c>null</c> quando esgotou.</summary>
    public static DateTimeOffset? NextAttemptFor(DateTimeOffset issuedAt, int failedAttempts) =>
        failedAttempts >= 1 && failedAttempts <= RetryOffsets.Length ? issuedAt + RetryOffsets[failedAttempts - 1] : null;

    /// <summary>Registra a falha de uma tentativa agendada e publica <c>PaymentFailed</c> ou <c>PaymentRetriesExhausted</c>.</summary>
    public void RegisterScheduledFailure(int attempt, string failureCode, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return;
        }

        Attempts++;
        NextAttemptAt = NextAttemptFor(IssuedAt, Attempts);
        if (NextAttemptAt is null)
        {
            ExhaustedAt = now;
            Raise(new Contracts.PaymentRetriesExhausted(TenantId, InvoiceId, now));
        }
        else
        {
            Raise(new Contracts.PaymentFailed(TenantId, InvoiceId, attempt, failureCode, NextAttemptAt, now));
        }
    }

    /// <summary>Uma tentativa manual falhou: a agenda continua de onde estava.</summary>
    public void RegisterManualFailure(int attempt, string failureCode, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return;
        }

        NextAttemptAt = NextAttemptFor(IssuedAt, Attempts);
        Raise(new Contracts.PaymentFailed(TenantId, InvoiceId, attempt, failureCode, NextAttemptAt, now));
    }

    /// <summary>Uma tentativa foi criada: a agenda pausa até o resultado chegar.</summary>
    public void Pause() => NextAttemptAt = null;

    public void Resolve(DateTimeOffset now)
    {
        ResolvedAt = now;
        NextAttemptAt = null;
    }
}
