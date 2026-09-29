using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Processing;

public interface IStampCompletionHandler
{
    Task ProcessAsync(
        string tenantId,
        Guid accountId,
        string contactKey,
        CachedRule rule,
        string eventId,
        CancellationToken ct);
}
