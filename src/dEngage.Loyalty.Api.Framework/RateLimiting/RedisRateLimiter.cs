using StackExchange.Redis;

namespace dEngage.Loyalty.Api.Framework.RateLimiting;

// Shared across every API instance via Redis (see plan §6: scalability), so a per-tenant limit is
// enforced correctly regardless of how many instances are running behind the load balancer.
public sealed class RedisRateLimiter(IConnectionMultiplexer redis) : IRateLimiter
{
    public async Task<bool> TryAcquireAsync(string bucket, string tenantId, int limitPerMinute, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var window = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var key = $"ratelimit:{bucket}:{tenantId}:{window}";

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(90));

        return count <= limitPerMinute;
    }
}
