using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Processing;

// Ledger audit trail: which rule (and which version of its config) produced a given ledger
// entry, with a snapshot of the conditions/calculation actually evaluated — not just the
// bare RuleId + free-text Metadata a LedgerEntry carries today.
public interface IRuleFireAuditWriter
{
    // Idempotent: a second call for the same (tenantId, sourceEventId, ruleId) is a no-op,
    // matching the ledger's own idempotency-key semantics for the entry it explains.
    // Does NOT SaveChanges — the caller's transaction owns persistence.
    Task RecordAsync(
        string tenantId,
        Guid ruleId,
        int ruleVersion,
        string sourceEventId,
        string contactKey,
        ConditionTree? conditions,
        RuleCalculation calculation,
        decimal resultingDelta,
        Guid? ledgerEntryId,
        string? resolutionSnapshot,
        CancellationToken ct);
}
