using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.RuleEngine.Calculation;

public sealed class StampRuleHandler : IRuleTypeHandler
{
    public string RuleType => RuleTypes.StampRule;

    // A 0 TL order earns no stamp. A rule may forget to enforce an amount filter via condition;
    // last line of defense at this level.
    public decimal Compute(RuleCalculation calculation, EvaluationEvent evt) =>
        evt.Amount > 0 ? 1m : 0m;
}
