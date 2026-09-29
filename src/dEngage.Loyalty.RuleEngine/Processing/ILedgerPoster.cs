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
}
