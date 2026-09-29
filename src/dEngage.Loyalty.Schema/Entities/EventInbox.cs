using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Schema.Entities;

public class EventInbox
{
    public string EventId { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string Status { get; set; } = InboxStatus.Pending;
    public string? Error { get; set; }
}
