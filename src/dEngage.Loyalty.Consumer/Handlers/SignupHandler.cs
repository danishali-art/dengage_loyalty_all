using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Consumer.Handlers;

// CR-01/CR-02: Earn/Behavioural, OncePerCustomer. Cardinality is enforced by rule config
// (CR-09 guarantee #2), not by this handler — it only routes to the generic rule-match pipeline,
// same shape as OrderCreatedHandler.
public class SignupHandler(ICampaignEvaluationService campaignEval) : IEventHandler
{
    public string? EventType => EventTypes.Signup;

    public Task HandleAsync(EventEnvelope envelope, CancellationToken ct) =>
        campaignEval.EvaluateAsync(envelope, ct);
}
