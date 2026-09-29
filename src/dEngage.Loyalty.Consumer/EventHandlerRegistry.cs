using dEngage.Loyalty.Engine.Framework.Consumers;

namespace dEngage.Loyalty.Consumer;

public sealed class EventHandlerRegistry : IEventHandlerRegistry
{
    private readonly Dictionary<string, IEventHandler> _handlers;
    private readonly IEventHandler _fallback;

    public EventHandlerRegistry(IEnumerable<IEventHandler> handlers)
    {
        var all = handlers.ToList();
        _handlers = all
            .Where(h => h.EventType is not null)
            .ToDictionary(h => h.EventType!);
        _fallback = all.Single(h => h.EventType is null);
    }

    public IEventHandler Resolve(string eventType) =>
        _handlers.TryGetValue(eventType, out var handler) ? handler : _fallback;
}
