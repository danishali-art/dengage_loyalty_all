using dEngage.Loyalty.Engine.Framework.Events;

namespace dEngage.Loyalty.Ledger;

// The concrete adapter IEventPublisherSink's remarks describe: forwards straight to
// IOutboxService.Enqueue, whose signature this interface was deliberately shaped to mirror.
public sealed class OutboxEventPublisherSink(IOutboxService outbox) : IEventPublisherSink
{
    public void Publish(string tenantId, string eventType, string contactKey, object data, string? dedupKey = null) =>
        outbox.Enqueue(tenantId, eventType, contactKey, data, dedupKey);
}
