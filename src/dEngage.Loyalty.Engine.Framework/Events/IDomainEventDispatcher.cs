namespace dEngage.Loyalty.Engine.Framework.Events;

// Single typed path for raising a domain event, replacing each service hand-rolling its own
// outbox payload inline. Resolves the matching IOutboxTranslator<TEvent> at call time.
public interface IDomainEventDispatcher
{
    void Raise<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent;
}
