using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.RuleEngine.Calculation;

// CR-02: Adjust, Operator source, any target (POINTS/CASH — STAMP retired by CR 2026-10-05,
// TIER_POINTS deferred, see plan). Sign-preserving: an operator adjustment can credit or debit. The event payload's
// amount takes precedence (each points.adjusted event carries its own operator-supplied
// amount); calculation.FixedValue is a fallback for a rule configured with one fixed
// correction amount every time it fires.
public sealed class ManualAdjustmentRuleHandler : IRuleTypeHandler
{
    public string RuleType => RuleTypes.ManualAdjustmentRule;

    public decimal Compute(RuleCalculation calculation, EvaluationEvent evt) =>
        evt.Amount != 0 ? evt.Amount : calculation.FixedValue ?? 0;
}
