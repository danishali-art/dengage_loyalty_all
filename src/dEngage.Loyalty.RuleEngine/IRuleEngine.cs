using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine;

public interface IRuleEngine
{
    Task ProcessEventAsync(
        string tenantId,
        Guid programId,
        string eventId,
        EvaluationEvent evt,
        CancellationToken ct = default);
}
