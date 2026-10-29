using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Subscribely.Modules.Metering.Quota;

/// <summary>Janela mensal de calendário UTC: chave do contador, período e instante do reset.</summary>
internal readonly record struct QuotaPeriod(string Name, DateTimeOffset ResetsAt)
{
    public static QuotaPeriod At(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var start = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return new QuotaPeriod(start.ToString("yyyy-MM", CultureInfo.InvariantCulture), start.AddMonths(1));
    }

    public static bool TryParse(string name, out QuotaPeriod period)
    {
        period = default;
        if (!DateTimeOffset.TryParseExact(name, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start))
        {
            return false;
        }

        period = At(start);
        return true;
    }

    public string CounterKey(Guid tenantId) => $"quota:{tenantId}:{Name.Replace("-", string.Empty, StringComparison.Ordinal)}";

    public string AlertKey(Guid tenantId, int threshold) =>
        string.Create(CultureInfo.InvariantCulture, $"quota-alert:{tenantId}:{Name}:{threshold}");

    /// <summary>Segundos até o reset, arredondado para cima (valor do <c>Retry-After</c>).</summary>
    public long SecondsUntilReset(DateTimeOffset now) => Math.Max(1, (long)Math.Ceiling((ResetsAt - now).TotalSeconds));
}

/// <param name="Allowed">A requisição pode seguir.</param>
/// <param name="Used">Contagem após esta requisição; <c>null</c> quando o Redis está fora (fail-open).</param>
internal readonly record struct QuotaDecision(bool Allowed, long? Used, long Limit, QuotaPeriod Period)
{
    public long Remaining => Used is { } used ? Math.Max(0, Limit - used) : Limit;
}

/// <summary>
/// Contador mensal no Redis com check-and-increment atômico (Lua, uma ida ao servidor): a requisição
/// rejeitada não incrementa. Redis fora do ar → fail-open com warning e métrica <c>quota_check_failed</c>.
/// </summary>
internal sealed partial class QuotaCounter(IConnectionMultiplexer redis, ILogger<QuotaCounter> logger)
{
    public static readonly TimeSpan KeyTtl = TimeSpan.FromDays(35);

    private const string Script = """
        local current = tonumber(redis.call('GET', KEYS[1]) or '0')
        if current >= tonumber(ARGV[1]) then return -1 end
        local n = redis.call('INCR', KEYS[1])
        if n == 1 then redis.call('EXPIRE', KEYS[1], ARGV[2]) end
        return n
        """;

    private static readonly Meter Meter = new("Subscribely.Metering");
    private static readonly Counter<long> CheckFailed = Meter.CreateCounter<long>("quota_check_failed");

    public async Task<QuotaDecision> TryConsumeAsync(Guid tenantId, long limit, DateTimeOffset now)
    {
        var period = QuotaPeriod.At(now);
        try
        {
            var result = (long)await redis.GetDatabase().ScriptEvaluateAsync(
                Script, [new RedisKey(period.CounterKey(tenantId))], [limit, (long)KeyTtl.TotalSeconds]);
            return result < 0
                ? new QuotaDecision(false, limit, limit, period)
                : new QuotaDecision(true, result, limit, period);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            CheckFailed.Add(1);
            LogQuotaUnavailable(logger, ex, tenantId);
            return new QuotaDecision(true, null, limit, period);
        }
    }

    /// <summary>Uso atual do período; <c>null</c> se o Redis estiver fora.</summary>
    public async Task<long?> CurrentAsync(Guid tenantId, QuotaPeriod period)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(period.CounterKey(tenantId));
            return value.IsNullOrEmpty ? 0 : (long)value;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            LogQuotaUnavailable(logger, ex, tenantId);
            return null;
        }
    }

    /// <summary><c>SET NX</c>: só a primeira chamada por threshold e período devolve <c>true</c>.</summary>
    public async Task<bool> TryClaimAlertAsync(Guid tenantId, QuotaPeriod period, int threshold)
    {
        try
        {
            return await redis.GetDatabase().StringSetAsync(period.AlertKey(tenantId, threshold), 1, KeyTtl, When.NotExists);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            LogQuotaUnavailable(logger, ex, tenantId);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quota check unavailable (Redis) for tenant {TenantId}; allowing request")]
    private static partial void LogQuotaUnavailable(ILogger logger, Exception exception, Guid tenantId);
}
