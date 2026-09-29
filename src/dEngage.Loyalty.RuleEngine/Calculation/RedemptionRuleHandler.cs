using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.RuleEngine.Calculation;

// CR-02: Burn, targets POINTS only. Debits the points amount carried on the event
// (evt.Amount — same convention every other handler in this registry uses), gated by
// calculation.MinRedeem. calculation.Ratio ("points per currency unit") is accepted and
// persisted for configuration parity with A3, but this handler only performs the debit leg —
// crediting a linked CASH account, as the legacy PointsRedeemHandler does, stays out of scope
// for this rule type (see docs/scope-changes changelog: burn/reverse rule types are additive,
// not a replacement of the existing points.redeem flow).
public sealed class RedemptionRuleHandler : IRuleTypeHandler
{
    public string RuleType => RuleTypes.RedemptionRule;

    public decimal Compute(RuleCalculation calculation, EvaluationEvent evt)
    {
        if (evt.Amount <= 0) return 0;
        if (calculation.MinRedeem.HasValue && evt.Amount < calculation.MinRedeem.Value) return 0;
        return -evt.Amount;
    }
}
