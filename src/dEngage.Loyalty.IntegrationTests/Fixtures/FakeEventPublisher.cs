using System.Collections.Concurrent;
using dEngage.Loyalty.Api.Framework.Messaging;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// Stands in for RabbitMqEventPublisher in the test host — records what the API tried to
// publish instead of opening a real broker connection, so API-layer tests can assert on
// "was this event accepted for publishing" without a running RabbitMQ.
public sealed class FakeEventPublisher : IEventPublisher
{
    public ConcurrentQueue<EventEnvelope> Published { get; } = new();

    public Task PublishAsync(EventEnvelope envelope, CancellationToken ct)
    {
        Published.Enqueue(envelope);
        return Task.CompletedTask;
    }
}
