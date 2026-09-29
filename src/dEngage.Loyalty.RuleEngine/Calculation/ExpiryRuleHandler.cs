using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.RuleEngine.Calculation;

// CR-02: Adjust, targets POINTS only, Scheduled source. The FIFO/LIFO lot-age computation
// (calculation.AgeDays/.Order) is NOT done here — IRuleTypeHandler is a pure function of
// (calculation, event) with no ledger access, so it cannot walk point lots itself. The
// publisher of the points.expired event is responsible for computing the lot-based amount
// and carrying it as evt.Amount; this handler only negates it. Wiring that publisher (rerouting
// the existing PointsExpirationJob through inbound ingestion instead of posting directly) is a
// separate, higher-risk follow-up — see the plan's Top Risks. Until that publisher exists, this
// handler is unreachable (no points.expired event is ever emitted), matching
// PointsExpiredHandler's current inert state.
public sealed class ExpiryRuleHandler : IRuleTypeHandler
{
    public string RuleType => RuleTypes.ExpiryRule;

    public decimal Compute(RuleCalculation calculation, EvaluationEvent evt) =>
        evt.Amount > 0 ? -evt.Amount : 0;
}
