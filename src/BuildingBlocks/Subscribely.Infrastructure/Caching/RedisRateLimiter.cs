using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Subscribely.Infrastructure.Caching;

/// <summary>Janela fixa distribuída (INCR + EXPIRE no Redis). Fail-open se o Redis estiver fora.</summary>
public sealed partial class RedisRateLimiter(IConnectionMultiplexer redis, ILogger<RedisRateLimiter> logger)
{
    private const string Script = """
        local n = redis.call('INCR', KEYS[1])
        if n == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
        return n
        """;

    /// <returns><c>true</c> se a ação está dentro do limite.</returns>
    public async Task<bool> TryAcquireAsync(string key, int limit, TimeSpan window)
    {
        try
        {
            var count = (long)await redis.GetDatabase().ScriptEvaluateAsync(
                Script, [new RedisKey($"ratelimit:{key}")], [(long)window.TotalSeconds]);
            return count <= limit;
        }
        catch (RedisException ex)
        {
            LogRedisUnavailable(logger, ex);
            return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limiter unavailable (Redis); allowing request")]
    private static partial void LogRedisUnavailable(ILogger logger, Exception exception);
}
