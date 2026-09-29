using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Processing;

// CR-02: ReversalRule bypasses WinnerSelector/LedgerPoster — its target account is inherited
// from the original posting being reversed (A3), not configured on the rule. NOT wired into
// the live order.refunded path in this milestone — see docs/scope-changes changelog: that
// event already has an unconditional legacy reversal path (RefundService via
// OrderRefundedHandler), and matching both would double-reverse the same original posting.
public interface IReversalRuleProcessor
{
    Task ProcessAsync(
        string tenantId,
        string eventId,
        IReadOnlyList<CachedRule> matchedReversalRules,
        EvaluationEvent evt,
        ConditionContext context,
        CancellationToken ct);
}
