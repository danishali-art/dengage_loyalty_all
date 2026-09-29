using dEngage.Loyalty.RuleEngine.Cache;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Processing;

public sealed class LimitCounterSync(ILimitCacheService limitCache, ILogger<LimitCounterSync> logger) : ILimitCounterSync
{
    // Update Redis counters after commit.
    // If an increment fails, invalidate the counter: the next Get* rebuilds it from
    // the DB. Otherwise a stale counter could grant the user extra points.
    public async Task SyncAsync(string tenantId, string contactKey, IReadOnlyList<AppliedRule> appliedRules)
    {
        foreach (var (rule, delta, _, _) in appliedRules)
        {
            try
            {
                await limitCache.IncrementAsync(tenantId, rule.Id, contactKey, delta);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "RuleEngine: [{Tenant}] limit increment failed for rule={Rule} contact={Contact} — invalidating",
                    tenantId, rule.Name, contactKey);
                try
                {
                    await limitCache.InvalidateAsync(tenantId, rule.Id, contactKey);
                }
                catch (Exception invEx)
                {
                    logger.LogError(invEx,
                        "RuleEngine: [{Tenant}] limit invalidate also failed for rule={Rule} contact={Contact}",
                        tenantId, rule.Name, contactKey);
                }
            }
        }
    }
}
