namespace dEngage.Loyalty.Engine.Framework.Events;

// Marker for an internal fact worth recording/publishing (e.g. RuleFiredEvent, TierChangedEvent).
// Deliberately minimal — event-specific data lives on the concrete event type, not here.
public interface IDomainEvent
{
    string TenantId { get; }
    DateTime OccurredAtUtc { get; }
}
