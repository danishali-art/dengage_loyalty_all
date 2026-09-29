using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Campaigns;

// Cached, per-program campaign config — the streak equivalent of CachedRule, but kept in its
// own cache/type entirely rather than as a nullable property on the generic rule DTO. Today's
// `rules` table still stores every campaign's config in the same row as earn rules (a schema
// split is explicitly deferred — see the refactor plan's Phase E), so a strongly-typed
// `Streak` property here is a pragmatic middle ground: fully separate at the runtime/cache/
// evaluation layer (the actual complaint being fixed), without inventing a generic
// raw-JSON-per-module parsing system for campaign types that don't exist yet. A second
// campaign type would add one more nullable property here, the same way CachedRule.Calculation
// grew new rule-type variants — an acceptable, minor, additive change, not a redesign.
public sealed class CachedCampaignConfig
{
    public Guid Id { get; set; }
    public Guid ProgramId { get; set; }
    public string Name { get; set; } = default!;
    public string CampaignType { get; set; } = default!;
    public string Trigger { get; set; } = default!;
    public List<ConditionClause>? Conditions { get; set; }
    public Guid TargetAccountTypeId { get; set; }
    public DateTime? ActiveFrom { get; set; }
    public DateTime? ActiveTo { get; set; }
    public StreakConfig? Streak { get; set; }
}
