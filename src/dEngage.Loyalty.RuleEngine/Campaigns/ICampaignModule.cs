using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Campaigns;

// Implemented once per CampaignTypes.* constant (today: just "streak"). A campaign module
// owns its own state end to end — its trigger/condition match is still done generically by
// RuleMatcher/ConditionEvaluator (same as earn rules), but everything past that (progress
// tracking, completion, rewards) is the module's own concern, not RuleEngine's.
public interface ICampaignModule
{
    string CampaignType { get; }

    Task<CampaignOutcome> EvaluateAsync(CampaignEvaluationRequest request, CancellationToken ct);
}

public sealed record CampaignEvaluationRequest(
    string TenantId,
    string EventId,
    CachedCampaignConfig Config,
    EvaluationEvent Evt);

public sealed record CampaignOutcome(bool Earned)
{
    public static readonly CampaignOutcome NotEarned = new(false);
}
