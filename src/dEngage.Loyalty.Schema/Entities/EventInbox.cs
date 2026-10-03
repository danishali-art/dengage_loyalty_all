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

    // CR 2026-10-02 (Customer 360): the event data's contact_key, recorded on arrival so the
    // customer view can list a customer's events. Null for events received before the CR and
    // for events that carry no contact_key.
    public string? ContactKey { get; set; }
}
