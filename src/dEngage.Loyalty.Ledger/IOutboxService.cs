using dEngage.Loyalty.Schema.Entities;

namespace dEngage.Loyalty.Ledger;

public interface IOutboxService
{
    Task<OutboxEvent> Enqueue(
        string tenantId,
        string eventType,
        string contactKey,
        object data,
        string? dedupKey = null,
        CancellationToken ct = default);
}
