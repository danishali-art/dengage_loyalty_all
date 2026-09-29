namespace dEngage.Loyalty.Shared.Events;

public class EventEnvelope
{
    public string EventId { get; init; } = default!;
    public string EventType { get; init; } = default!;
    public string Tenant { get; init; } = default!;
    public DateTime OccurredAt { get; init; }
    public string Version { get; init; } = "1";
    public System.Text.Json.JsonElement Data { get; init; }
}
