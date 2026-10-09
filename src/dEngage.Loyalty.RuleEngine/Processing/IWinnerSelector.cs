using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Processing;

public interface IWinnerSelector
{
    Task<IReadOnlyList<AppliedRule>> SelectAsync(
        string tenantId,
        IReadOnlyList<CachedRule> earnRules,
        EvaluationEvent evt,
        ConditionContext context,
        CancellationToken ct,
        // CR 2026-10-06 Phase 4: the program's default_rounding, inherited by rules whose own
        // Configuration.rounding is unset. Null = Down.
        string? programDefaultRounding = null);
}
