using Microsoft.Extensions.DependencyInjection;

namespace dEngage.Loyalty.Engine.Framework.Events;

// A domain event with no registered translator is dropped silently — same "not every event
// needs an outbound side effect" default as today's ad-hoc outbox.Enqueue call sites, just
// centralized. Register a translator only for events that should actually go out.
public sealed class DomainEventDispatcher(IServiceProvider services, IEventPublisherSink sink) : IDomainEventDispatcher
{
    public void Raise<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
    {
        var translator = services.GetService<IOutboxTranslator<TEvent>>();
        translator?.Translate(domainEvent, sink);
    }
}
