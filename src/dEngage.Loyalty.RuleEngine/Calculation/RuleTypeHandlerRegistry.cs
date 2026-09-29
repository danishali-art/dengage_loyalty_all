using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Calculation;

public sealed class RuleTypeHandlerRegistry : IRuleTypeHandlerRegistry
{
    private readonly Dictionary<string, IRuleTypeHandler> _handlers;

    public RuleTypeHandlerRegistry(IEnumerable<IRuleTypeHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.RuleType);
    }

    public IRuleTypeHandler Resolve(string ruleType) =>
        _handlers.TryGetValue(ruleType, out var handler) ? handler : ZeroDeltaHandler.Instance;

    // Same fallback as today's `Calculator.Compute` switch default (`_ => 0`) for a rule
    // type with no registered handler.
    private sealed class ZeroDeltaHandler : IRuleTypeHandler
    {
        public static readonly ZeroDeltaHandler Instance = new();

        public string RuleType => string.Empty;
        public decimal Compute(RuleCalculation calculation, EvaluationEvent evt) => 0;
    }
}
