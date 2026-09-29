namespace dEngage.Loyalty.Schema.Entities;

// One row per rule that actually fired (winner or applied stackable — matching the same
// selective granularity ledger entries already have, not every rule evaluated). Written in
// the same transaction as the ledger entry it explains, so it is exactly as durable.
public class RuleFireAudit
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RuleId { get; set; }

    // CR-09 (A10 guarantee #7): Rule.CurrentVersion at cache-load time — which config was
    // actually evaluated, which can differ from the rule's current version if it was edited
    // after the cache last refreshed. A real version number (RuleVersion table), not a
    // timestamp proxy — see RuleEngine.Models.CachedRule.Version remarks.
    public int RuleVersion { get; set; }

    public string SourceEventId { get; set; } = default!;
    public string ContactKey { get; set; } = default!;
    public string? ConditionsSnapshot { get; set; }
    public string CalculationSnapshot { get; set; } = default!;
    public decimal ResultingDelta { get; set; }
    public Guid? LedgerEntryId { get; set; }

    // CR-06/A10 guarantee #9: per-wallet resolution detail for this posting — exclusivity
    // group and losing rule ids (for a base winner), or the multiplier chain and running total
    // before/after (for any rule whose delta was scaled) — see
    // RuleEngine.Processing.WinnerSelector.ResolutionSnapshot. Null for postings that didn't go
    // through stacking resolution (e.g. RedemptionRule/ManualAdjustmentRule).
    public string? ResolutionSnapshot { get; set; }

    public DateTime CreatedAt { get; set; }
}
