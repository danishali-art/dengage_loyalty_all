namespace dEngage.Loyalty.RuleEngine;

public interface ITierEvaluationService
{
    Task EvaluateAsync(
        string tenantId,
        string contactKey,
        Guid programId,
        Guid qualifyingAccountTypeId,
        string sourceEventId,
        CancellationToken ct = default);
}
