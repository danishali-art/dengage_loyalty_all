using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Schema.Entities;

public class OutboxEvent
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public Guid TenantId { get; set; }
    public string EventType { get; set; } = default!;
    public string ContactKey { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public string? DedupKey { get; set; }
    public string Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
