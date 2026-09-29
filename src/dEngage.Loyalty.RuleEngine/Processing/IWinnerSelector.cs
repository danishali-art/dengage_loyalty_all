using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Processing;

public interface IWinnerSelector
{
    Task<IReadOnlyList<AppliedRule>> SelectAsync(
        string tenantId,
        IReadOnlyList<CachedRule> earnRules,
        EvaluationEvent evt,
        ConditionContext context,
        CancellationToken ct);
}
