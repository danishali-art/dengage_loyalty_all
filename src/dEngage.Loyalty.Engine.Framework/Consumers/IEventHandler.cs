using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.Engine.Framework.Consumers;

// Implemented by each Consumer.Handlers.*Handler. EventType names the single built-in
// EventTypes.* constant this handler owns; null marks the fallback (today's GenericEventHandler)
// that IEventHandlerRegistry resolves to for anything not otherwise registered.
public interface IEventHandler
{
    string? EventType { get; }
    Task HandleAsync(EventEnvelope envelope, CancellationToken ct);
}
