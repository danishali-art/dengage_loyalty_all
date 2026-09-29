using System.Globalization;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace dEngage.Loyalty.RuleEngine.Cache;

public class LimitCacheService(
    IConnectionMultiplexer redis,
    LoyaltyDbContext db,
    ILogger<LimitCacheService> logger) : ILimitCacheService
{
    private readonly IDatabase _cache = redis.GetDatabase();

    // The total key is not kept forever — 90 days is enough since it can be rebuilt from the DB.
    private static readonly TimeSpan TotalKeyTtl = TimeSpan.FromDays(90);
    private static readonly TimeSpan DailyKeyTtl = TimeSpan.FromHours(25);

    private static string TotalKey(string tenantId, Guid ruleId, string contactKey) =>
        $"limit:{tenantId}:{ruleId}:{contactKey}:total";

    private static string DailyKey(string tenantId, Guid ruleId, string contactKey) =>
        $"limit:{tenantId}:{ruleId}:{contactKey}:{DateTime.UtcNow:yyyy-MM-dd}";

    public async Task<decimal> GetTotalAsync(string tenantId, Guid ruleId, string contactKey, CancellationToken ct = default)
    {
        var key = TotalKey(tenantId, ruleId, contactKey);
        var val = await _cache.StringGetAsync(key);

        if (val.HasValue)
            return decimal.Parse(val!, CultureInfo.InvariantCulture);

        return await RebuildTotalAsync(tenantId, ruleId, contactKey, key, ct);
    }

    public async Task<decimal> GetDailyAsync(string tenantId, Guid ruleId, string contactKey, CancellationToken ct = default)
    {
        var key = DailyKey(tenantId, ruleId, contactKey);
        var val = await _cache.StringGetAsync(key);

        if (val.HasValue)
            return decimal.Parse(val!, CultureInfo.InvariantCulture);

        return await RebuildDailyAsync(tenantId, ruleId, contactKey, key, ct);
    }

    // Increments the total and daily counters in one transaction. Both happen or neither does.
    // INCRBYFLOAT is used: limit clipping can produce a fractional delta (the (long) cast
    // silently truncated it) and integer INCR would fail on a rebuilt value like "12.5".
    public async Task IncrementAsync(string tenantId, Guid ruleId, string contactKey, decimal delta)
    {
        var totalKey = TotalKey(tenantId, ruleId, contactKey);
        var dailyKey = DailyKey(tenantId, ruleId, contactKey);
        var incr     = (double)delta;

        var tx = _cache.CreateTransaction();
        var totalTask   = tx.StringIncrementAsync(totalKey, incr);
        var totalTtl    = tx.KeyExpireAsync(totalKey, TotalKeyTtl, ExpireWhen.HasNoExpiry);
        var dailyTask   = tx.StringIncrementAsync(dailyKey, incr);
        var dailyTtl    = tx.KeyExpireAsync(dailyKey, DailyKeyTtl, ExpireWhen.HasNoExpiry);

        var committed = await tx.ExecuteAsync();
        if (!committed)
        {
            logger.LogWarning(
                "LimitCache: transaction not committed for rule={RuleId} contact={Contact} — invalidating keys",
                ruleId, contactKey);
            await _cache.KeyDeleteAsync(new RedisKey[] { totalKey, dailyKey });
            return;
        }

        await Task.WhenAll(totalTask, totalTtl, dailyTask, dailyTtl);
    }

    // Called when IncrementAsync fails at runtime: invalidate the counters so the
    // next GetTotal/GetDaily rebuilds them correctly from the DB.
    public async Task InvalidateAsync(string tenantId, Guid ruleId, string contactKey)
    {
        var totalKey = TotalKey(tenantId, ruleId, contactKey);
        var dailyKey = DailyKey(tenantId, ruleId, contactKey);
        await _cache.KeyDeleteAsync(new RedisKey[] { totalKey, dailyKey });
    }

    // Rebuild: net earnings = Earn + StampEarn + Refund (refund carries a negative delta).
    // Refund does not lower the limit counter at runtime (S02) — but after a Redis loss,
    // reopening the limit in the customer's favor is a behavior intentionally applied here (RB02).
    private async Task<decimal> RebuildTotalAsync(string tenantId, Guid ruleId, string contactKey, string key, CancellationToken ct)
    {
        var relevantReasons = new[]
        {
            dEngage.Loyalty.Shared.LedgerReason.Earn,
            dEngage.Loyalty.Shared.LedgerReason.StampEarn,
            dEngage.Loyalty.Shared.LedgerReason.Refund
        };

        var sum = await db.LedgerEntries
            .Where(x =>
                x.TenantId == tenantId &&
                x.ContactKey == contactKey &&
                x.RuleId == ruleId &&
                relevantReasons.Contains(x.Reason))
            .SumAsync(x => (decimal?)x.Delta, ct) ?? 0;

        if (sum < 0)
            logger.LogWarning(
                "LimitCache: total rebuild negative for rule={RuleId} contact={Contact} sum={Sum} — clamped to 0",
                ruleId, contactKey, sum);

        var net = Math.Max(0, sum);
        await _cache.StringSetAsync(key, net.ToString(CultureInfo.InvariantCulture), TotalKeyTtl);
        return net;
    }

    private async Task<decimal> RebuildDailyAsync(string tenantId, Guid ruleId, string contactKey, string key, CancellationToken ct)
    {
        var todayStart = DateTime.UtcNow.Date;
        var relevantReasons = new[]
        {
            dEngage.Loyalty.Shared.LedgerReason.Earn,
            dEngage.Loyalty.Shared.LedgerReason.StampEarn,
            dEngage.Loyalty.Shared.LedgerReason.Refund
        };

        var sum = await db.LedgerEntries
            .Where(x =>
                x.TenantId == tenantId &&
                x.ContactKey == contactKey &&
                x.RuleId == ruleId &&
                relevantReasons.Contains(x.Reason) &&
                x.CreatedAt >= todayStart)
            .SumAsync(x => (decimal?)x.Delta, ct) ?? 0;

        if (sum < 0)
            logger.LogWarning(
                "LimitCache: daily rebuild negative for rule={RuleId} contact={Contact} sum={Sum} — clamped to 0",
                ruleId, contactKey, sum);

        var net = Math.Max(0, sum);
        await _cache.StringSetAsync(key, net.ToString(CultureInfo.InvariantCulture), DailyKeyTtl);
        return net;
    }
}
