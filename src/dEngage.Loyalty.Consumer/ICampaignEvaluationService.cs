using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Consumer;

public interface ICampaignEvaluationService
{
    Task EvaluateAsync(EventEnvelope envelope, CancellationToken ct);
}
