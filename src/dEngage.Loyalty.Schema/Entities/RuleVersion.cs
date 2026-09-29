namespace dEngage.Loyalty.Schema.Entities;

// CR-09 (docs/scope-change-rules A10 guarantee #7): an immutable snapshot of a Rule as of one
// edit. Rule itself stays the "current" row RuleCacheService/matching read from (unchanged
// shape/behavior); every UpdateAsync call archives the PRE-edit state here as
// Rule.CurrentVersion before applying the edit and incrementing it — so version N's full
// config is always recoverable, not just inferable from whichever posting happened to audit it.
public class RuleVersion
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RuleId { get; set; }
    public int VersionNumber { get; set; }

    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Trigger { get; set; } = default!;
    public string? Conditions { get; set; }
    public string Calculation { get; set; } = default!;
    public Guid? TargetAccountTypeId { get; set; }
    public string? Limits { get; set; }
    public string? Configuration { get; set; }
    public int Priority { get; set; }
    public bool Stackable { get; set; }
    public string? ExclusivityGroup { get; set; }
    public string StackMode { get; set; } = default!;
    public DateTime? ActiveFrom { get; set; }
    public DateTime? ActiveTo { get; set; }

    // No scheduling UI exists yet (A8's "effective date" concept), so EffectiveFrom is always
    // the moment the edit was saved — a future CR that adds scheduled/future-dated edits would
    // change how this is set, not the shape of this table.
    public DateTime EffectiveFrom { get; set; }
    public DateTime CreatedAt { get; set; }
}
