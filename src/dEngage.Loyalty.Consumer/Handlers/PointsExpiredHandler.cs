using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Consumer.Handlers;

// CR-01/CR-02: Adjust/Scheduled. Routes to the generic rule-match pipeline — ExpiryRule (added
// in CR-02) is what actually posts. Not externally publishable (see EventsAppService); the
// internal publisher is added when PointsExpirationJob is rerouted through ingestion instead of
// posting directly (CR-02/CR-09 follow-up milestone). Until that publisher exists this handler
// is unreachable in production but is wired now so that follow-up is additive.
public class PointsExpiredHandler(ICampaignEvaluationService campaignEval) : IEventHandler
{
    public string? EventType => EventTypes.PointsExpired;

    public Task HandleAsync(EventEnvelope envelope, CancellationToken ct) =>
        campaignEval.EvaluateAsync(envelope, ct);
}
