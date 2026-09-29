namespace dEngage.Loyalty.RuleEngine.Calculation;

public interface IRuleTypeHandlerRegistry
{
    // Resolves to a 0-delta handler for an unrecognized rule type — same fallback as
    // today's `Calculator.Compute` switch default, never throws.
    IRuleTypeHandler Resolve(string ruleType);
}
