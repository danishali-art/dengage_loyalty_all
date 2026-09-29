using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Processing;

// CR-02: TransferRule bypasses WinnerSelector/LedgerPoster (dual-entry posting — see
// WinnerSelector's class remarks). NOT wired into the live points.transfer path in this
// milestone — see docs/scope-changes changelog: the event already has an unconditional legacy
// handler (PointsTransferHandler), and matching both would double-process a transfer.
public interface ITransferRuleProcessor
{
    Task ProcessAsync(
        string tenantId,
        string eventId,
        IReadOnlyList<CachedRule> matchedTransferRules,
        EvaluationEvent evt,
        ConditionContext context,
        CancellationToken ct);
}
