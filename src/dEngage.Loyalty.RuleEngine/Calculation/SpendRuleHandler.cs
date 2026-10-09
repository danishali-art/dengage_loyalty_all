using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.RuleEngine.Calculation;

public sealed class SpendRuleHandler : IRuleTypeHandler
{
    public string RuleType => RuleTypes.SpendRule;

    public decimal Compute(RuleCalculation calculation, EvaluationEvent evt) => Compute(calculation, evt, 0);

    // CR 2026-10-06 Phase 4: rate × amount, unrounded. Rounding happens once, in
    // WinnerSelector.ApplyRounding — to the target wallet's decimals, in the rule's (or the
    // program's) direction, Down by default — instead of here and again there (1.3.CL item 2 used
    // to round down here, which made a rule's Up / Nearest do nothing).
    public decimal Compute(RuleCalculation calculation, EvaluationEvent evt, int targetDecimals) =>
        calculation.Factor.HasValue ? evt.Amount * calculation.Factor.Value : 0;
}
