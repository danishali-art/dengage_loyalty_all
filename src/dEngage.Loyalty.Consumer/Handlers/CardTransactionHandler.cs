using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Consumer.Handlers;

// CR-01/CR-02: Earn/Behavioural, Unlimited — same generic rule-match routing as
// OrderCreatedHandler; MCC/amount/status conditions are evaluated by SpendRule/FixedBonusRule
// configs, not hardcoded here.
public class CardTransactionHandler(ICampaignEvaluationService campaignEval) : IEventHandler
{
    public string? EventType => EventTypes.CardTransaction;

    public Task HandleAsync(EventEnvelope envelope, CancellationToken ct) =>
        campaignEval.EvaluateAsync(envelope, ct);
}
