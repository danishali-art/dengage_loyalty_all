using System.Text.Json;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Ledger;

public class OutboxService(LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver) : IOutboxService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Only adds the row to the context, does not call SaveChanges — persisting is the
    // caller's transaction's job. If it does not commit in the same tx as the ledger write,
    // a "balance changed but the event never went out" (or the reverse) inconsistency arises.
    public async Task<OutboxEvent> Enqueue(
        string tenantId,
        string eventType,
        string contactKey,
        object data,
        string? dedupKey = null,
        CancellationToken ct = default)
    {
        var eventId = UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql);

        // The published envelope's Tenant field is the external slug — unchanged wire contract.
        var envelope = new
        {
            EventId = eventId.ToString(),
            EventType = eventType,
            Tenant = tenantId,
            OccurredAt = DateTime.UtcNow,
            Version = "1",
            Data = data
        };

        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var outboxEvent = new OutboxEvent
        {
            EventId = eventId,
            TenantId = tenantGuid,
            EventType = eventType,
            ContactKey = contactKey,
            Payload = JsonSerializer.Serialize(envelope, JsonOptions),
            DedupKey = dedupKey,
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        db.OutboxEvents.Add(outboxEvent);
        return outboxEvent;
    }
}
