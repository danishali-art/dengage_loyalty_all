using FluentAssertions;
using dEngage.Loyalty.Consumer;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Shared.Events;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

public class EventHandlerRegistryTests
{
    private sealed class FakeHandler(string? eventType) : IEventHandler
    {
        public string? EventType => eventType;
        public Task HandleAsync(EventEnvelope envelope, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public void Resolve_returns_the_handler_registered_for_that_event_type()
    {
        var orderCreated = new FakeHandler(EventTypes.OrderCreated);
        var cashAdded = new FakeHandler(EventTypes.CashAdded);
        var fallback = new FakeHandler(null);

        var registry = new EventHandlerRegistry([orderCreated, cashAdded, fallback]);

        registry.Resolve(EventTypes.OrderCreated).Should().BeSameAs(orderCreated);
        registry.Resolve(EventTypes.CashAdded).Should().BeSameAs(cashAdded);
    }

    [Fact]
    public void Resolve_falls_back_to_the_handler_with_a_null_EventType_for_unknown_types()
    {
        var orderCreated = new FakeHandler(EventTypes.OrderCreated);
        var fallback = new FakeHandler(null);

        var registry = new EventHandlerRegistry([orderCreated, fallback]);

        registry.Resolve("some.generic.event").Should().BeSameAs(fallback);
        registry.Resolve("card.transaction").Should().BeSameAs(fallback);
    }
}
