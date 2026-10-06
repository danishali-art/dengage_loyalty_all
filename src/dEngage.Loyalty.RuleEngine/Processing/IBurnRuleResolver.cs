using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Processing;

// CR 2026-10-05 item 1: points.redeem / points.transfer always run on a rule. Their handlers stay
// the only code that posts (transaction, row locks, redelivery guard, failure events); this seam
// only picks the rule and does the rule's own bookkeeping, so the generic engine never has to see
// a burn rule (RuleTypes.UsesWinnerSelectorPipeline excludes both types).
public interface IBurnRuleResolver
{
    // The highest-priority usable rule of ruleType for this event, or the failure reason:
    // candidates are Active, inside their window, on evt.EventType, and target the debited
    // wallet; conditions must pass and cooldown / max_customers / min_event_amount / budget must
    // not be exhausted. Exactly one rule applies — no stacking, no fall-through after the pick.
    Task<BurnRuleResolution> ResolveAsync(
        string tenantId,
        Guid programId,
        string ruleType,
        Guid sourceAccountTypeId,
        string eventId,
        EvaluationEvent evt,
        CancellationToken ct);

    // Race-safe budget reservation for the full amount, inside the caller's transaction (same
    // reserve-then-post order as LedgerPoster). A burn is never partially applied, so a budget
    // that can't take the whole amount is a refusal whatever on_breach says.
    Task<bool> TryReserveBudgetAsync(string tenantId, CachedRule rule, decimal amount, CancellationToken ct);

    // RuleFireAudit row (ruleId + ruleVersion) for the posting; added to the caller's
    // DbContext, saved with the caller's SaveChanges.
    Task RecordFireAsync(
        string tenantId,
        CachedRule rule,
        string eventId,
        string contactKey,
        decimal delta,
        Guid? ledgerEntryId,
        CancellationToken ct);
}

public sealed record BurnRuleResolution(CachedRule? Rule, string? FailureReason)
{
    public static BurnRuleResolution Found(CachedRule rule) => new(rule, null);
    public static BurnRuleResolution Failed(string reason) => new(null, reason);
}
