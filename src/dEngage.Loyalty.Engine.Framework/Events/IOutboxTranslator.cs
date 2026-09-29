namespace dEngage.Loyalty.Engine.Framework.Events;

// One implementation per concrete IDomainEvent, owning that event's exact outbound JSON shape
// (so existing outbound consumers see byte-identical payloads after Phase F lands).
public interface IOutboxTranslator<in TEvent> where TEvent : IDomainEvent
{
    void Translate(TEvent domainEvent, IEventPublisherSink sink);
}
