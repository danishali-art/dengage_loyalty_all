namespace dEngage.Loyalty.Engine.Framework.Consumers;

// Replaces EventConsumerWorker.DispatchAsync's switch statement. Built once at DI composition
// time from every registered IEventHandler, keyed by EventType, with the single handler whose
// EventType is null kept aside as the fallback for anything not otherwise registered.
public interface IEventHandlerRegistry
{
    IEventHandler Resolve(string eventType);
}
