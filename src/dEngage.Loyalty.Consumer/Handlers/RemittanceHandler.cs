using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Consumer.Handlers;

// CR-01/CR-02: Earn/Behavioural, Unlimited — see CardTransactionHandler.
public class RemittanceHandler(ICampaignEvaluationService campaignEval) : IEventHandler
{
    public string? EventType => EventTypes.Remittance;

    public Task HandleAsync(EventEnvelope envelope, CancellationToken ct) =>
        campaignEval.EvaluateAsync(envelope, ct);
}
