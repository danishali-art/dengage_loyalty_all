namespace dEngage.Loyalty.Schema.Entities;

public class EventLog
{
    public string TenantId { get; set; } = default!;
    public string EventId { get; set; } = default!;
    public string ContactKey { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public DateTime OccurredAt { get; set; }
}
