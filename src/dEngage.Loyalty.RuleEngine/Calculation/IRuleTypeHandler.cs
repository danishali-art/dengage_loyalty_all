using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Calculation;

// One implementation per RuleTypes.* constant. Replaces Calculator.Compute's switch —
// adding a rule type is now "add a class + register it", not "edit a switch".
public interface IRuleTypeHandler
{
    string RuleType { get; }
    decimal Compute(RuleCalculation calculation, EvaluationEvent evt);

    // 1.3.CL item 2: targetDecimals is the target wallet's configured `decimals`. Only Spend
    // rules use it today (§5 g keeps other types out of scope), so every other handler inherits
    // this default and ignores it.
    decimal Compute(RuleCalculation calculation, EvaluationEvent evt, int targetDecimals) => Compute(calculation, evt);
}
