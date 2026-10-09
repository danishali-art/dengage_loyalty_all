using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Processing;

// Caller (RuleEngine) must not call this with an empty appliedRules list — an empty list
// would still enqueue a PointsEarned event with no accounts, which today's guard prevents.
public interface ILedgerPoster
{
    Task PostAsync(
        string tenantId,
        string eventId,
        EvaluationEvent evt,
        IReadOnlyList<AppliedRule> appliedRules,
        CancellationToken ct);

    // CR 2026-10-06 R15: true when an earlier delivery of this event already posted (or held) one
    // of these rules. PostAsync writes all of an event's rules in one transaction, so one is enough.
    Task<bool> HasPostedAsync(
        string tenantId,
        string eventId,
        EvaluationEvent evt,
        IEnumerable<CachedRule> rules,
        CancellationToken ct);
}
