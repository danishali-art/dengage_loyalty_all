using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.RuleEngine.Calculation;

public sealed class SpendRuleHandler : IRuleTypeHandler
{
    // The ledger stores numeric(20,4); the account-type validator caps decimals at 4 too.
    private const int MaxDecimals = 4;

    public string RuleType => RuleTypes.SpendRule;

    public decimal Compute(RuleCalculation calculation, EvaluationEvent evt) => Compute(calculation, evt, 0);

    // 1.3.CL item 2: round DOWN to the target wallet's decimal places (previously always a whole
    // number). Down, not nearest, so a customer is never credited more than rate × amount.
    public decimal Compute(RuleCalculation calculation, EvaluationEvent evt, int targetDecimals)
    {
        if (!calculation.Factor.HasValue) return 0;
        var scale = Math.Clamp(targetDecimals, 0, MaxDecimals) switch { 0 => 1m, 1 => 10m, 2 => 100m, 3 => 1000m, _ => 10000m };
        return Math.Floor(evt.Amount * calculation.Factor.Value * scale) / scale;
    }
}
