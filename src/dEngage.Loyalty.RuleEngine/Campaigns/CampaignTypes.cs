namespace dEngage.Loyalty.RuleEngine.Campaigns;

// Campaign types are a separate namespace of values from RuleTypes.* — a campaign is not
// a rule type, even though today's `rules` table still stores its config in the same row
// (see CachedCampaignConfig's remarks for why that persistence detail hasn't changed yet).
public static class CampaignTypes
{
    public const string Streak = "streak";
}
