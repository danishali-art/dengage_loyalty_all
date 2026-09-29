namespace dEngage.Loyalty.RuleEngine.Models;

public class CachedRule
{
    public Guid Id { get; set; }
    public Guid ProgramId { get; set; }
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Trigger { get; set; } = default!;
    // CR-05: grouped AND/OR tree (Rules/Card Buckets) — see ConditionTree.cs. Not the flat
    // ConditionClause list Streak Campaigns still use (they have their own cache type,
    // CachedCampaignConfig).
    public ConditionTree? Conditions { get; set; }
    public RuleCalculation Calculation { get; set; } = default!;
    // CR-08 — null means every setting is at its A8 default.
    public RuleSettings? Configuration { get; set; }
    // Null only for ReversalRule (CR-02) — see Schema.Entities.Rule.TargetAccountTypeId.
    public Guid? TargetAccountTypeId { get; set; }
    // 1.3.CL item 2: the target wallet's config `decimals`, captured at cache-load time. 0 (the
    // default) reproduces the pre-1.3.CL whole-number Spend rounding for any cache entry written
    // before this field existed.
    public int TargetDecimals { get; set; }
    public RuleLimits? Limits { get; set; }
    public int Priority { get; set; }
    public bool Stackable { get; set; }
    // CR-06 — see Schema.Entities.Rule remarks.
    public string? ExclusivityGroup { get; set; }
    public string StackMode { get; set; } = dEngage.Loyalty.Shared.RuleStackMode.Additive;
    public DateTime? ActiveFrom { get; set; }
    public DateTime? ActiveTo { get; set; }

    // CR-09 (A10 guarantee #7): Rule.CurrentVersion at cache-load time — a real version
    // number, not a timestamp proxy — used for audit purposes (which config was actually
    // evaluated, which can differ from the rule's current version if it changed after the
    // cache last refreshed).
    public int Version { get; set; }
}
