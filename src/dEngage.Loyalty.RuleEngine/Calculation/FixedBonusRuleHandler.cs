using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.RuleEngine.Calculation;

public sealed class FixedBonusRuleHandler : IRuleTypeHandler
{
    public string RuleType => RuleTypes.FixedBonusRule;

    public decimal Compute(RuleCalculation calculation, EvaluationEvent evt) =>
        calculation.FixedValue ?? 0;
}
