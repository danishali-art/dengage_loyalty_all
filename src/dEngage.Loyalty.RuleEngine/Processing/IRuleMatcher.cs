using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Processing;

public interface IRuleMatcher
{
    Task<MatchedRules> MatchAsync(string tenantId, Guid programId, EvaluationEvent evt, CancellationToken ct);
}

public sealed record MatchedRules(IReadOnlyList<CachedRule> EarnRules, IReadOnlyList<CachedCampaignConfig> CampaignConfigs)
{
    public bool IsEmpty => EarnRules.Count == 0 && CampaignConfigs.Count == 0;
}
