using System.Text.Json;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Api.Framework.Messaging;

// Centralizes envelope construction so every ingestion-gateway route builds EventEnvelope the same
// way (idempotency-key handling, tenant stamping, UTC OccurredAt default) instead of each route
// duplicating this logic.
public static class EventEnvelopeFactory
{
    public static EventEnvelope Create<TData>(
        string eventType,
        string tenantId,
        TData data,
        string? idempotencyKey,
        DateTime? occurredAt)
    {
        var json = JsonSerializer.SerializeToElement(data, JsonConventions.EventDataOptions);

        return new EventEnvelope
        {
            EventId = idempotencyKey ?? Guid.NewGuid().ToString(),
            EventType = eventType,
            Tenant = tenantId,
            OccurredAt = occurredAt ?? DateTime.UtcNow,
            Version = "1",
            Data = json
        };
    }
}
