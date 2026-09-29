using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Processing;

public sealed class RuleMatcher(
    IRuleCacheService ruleCache,
    ICampaignConfigCacheService campaignConfigCache) : IRuleMatcher
{
    public async Task<MatchedRules> MatchAsync(string tenantId, Guid programId, EvaluationEvent evt, CancellationToken ct)
    {
        // Active window is checked at evaluation time (second precision) — the caches
        // hold all Active-status rows regardless of window.
        var now = DateTime.UtcNow;

        var rules = await ruleCache.GetRulesAsync(tenantId, programId, ct);
        var earnRules = rules
            .Where(r => r.Trigger == evt.EventType &&
                        (r.ActiveFrom == null || r.ActiveFrom <= now) &&
                        (r.ActiveTo == null || now < r.ActiveTo))
            .ToList();

        var campaignConfigs = await campaignConfigCache.GetConfigsAsync(tenantId, programId, ct);
        var matchedCampaigns = campaignConfigs
            .Where(c => c.Trigger == evt.EventType &&
                        (c.ActiveFrom == null || c.ActiveFrom <= now) &&
                        (c.ActiveTo == null || now < c.ActiveTo))
            .ToList();

        return new MatchedRules(earnRules, matchedCampaigns);
    }
}
