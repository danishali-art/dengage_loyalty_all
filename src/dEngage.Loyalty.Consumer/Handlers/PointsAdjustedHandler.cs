using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Consumer.Handlers;

// CR-01/CR-02: Adjust/Operator. Routes to the generic rule-match pipeline like every other
// rule-driven event — ManualAdjustmentRule (added in CR-02) is what actually posts. Until a
// ManualAdjustmentRule exists for a tenant/program this is a no-op (RuleEngine.ProcessEventAsync
// bails when nothing matches). Never externally publishable to signup/kyc-style callers by
// itself — see EventsAppService/Shared.Events.EventTypes.IsExternallyPublishable (still true
// for Operator source; the operator-only nature is enforced by who holds the tenant API key,
// not by this handler).
public class PointsAdjustedHandler(ICampaignEvaluationService campaignEval) : IEventHandler
{
    public string? EventType => EventTypes.PointsAdjusted;

    public Task HandleAsync(EventEnvelope envelope, CancellationToken ct) =>
        campaignEval.EvaluateAsync(envelope, ct);
}
