using dEngage.Loyalty.Shared;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using dEngage.Loyalty.Contract;

namespace dEngage.Loyalty.Consumer;

// Platform alignment D12 (tenant fairness). q.loyalty is processed one event at a time for every
// tenant, so a burst from one tenant delays all the others. A tenant listed in
// RabbitMq:IsolatedTenants gets its own queue (q.loyalty.tenant.<slug>) with its own sequential
// worker. The main worker doesn't process that tenant's events; it moves them, in arrival order,
// to the tenant's queue — a broker round-trip instead of a full rule-engine and ledger run.
//
// Ordering: a tenant's events still go through exactly one FIFO queue at a time, so per-customer
// order is unchanged. Lanes of different tenants run in parallel, which is safe because every
// piece of processing state (inbox, ledger, accounts, limit counters, streaks, Redis keys) is
// keyed by tenant.
//
// Removing a tenant from the list: first make sure its q.loyalty.tenant.<slug> queue is empty
// (RabbitMQ management UI), or its remaining events would be processed after newer ones.
public static class TenantLanes
{
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(10);

    // Same dead-lettering as q.loyalty: a failed event in a lane lands in q.loyalty.dlq.
    public static string Declare(IModel channel, string tenantSlug)
    {
        var queue = MessagingTopology.TenantLaneQueue(tenantSlug);
        channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object> { ["x-dead-letter-exchange"] = MessagingTopology.DeadLetterExchange });
        return queue;
    }

    // Publishes the delivery unchanged (body, headers, message id, content type) to the tenant's
    // queue and waits for the broker's confirm. Only after this returns may the caller ack the
    // original delivery: if the process dies in between, the event is delivered twice and the
    // second copy is skipped by the EventInbox dedupe. `confirmChannel` must be in confirm mode
    // (ConfirmSelect) and used by one thread only.
    public static void Forward(IModel confirmChannel, BasicDeliverEventArgs delivery, string tenantSlug)
    {
        var properties = confirmChannel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = delivery.BasicProperties?.ContentType;
        properties.MessageId = delivery.BasicProperties?.MessageId;
        properties.Type = delivery.BasicProperties?.Type;
        if (delivery.BasicProperties?.Headers is { Count: > 0 } headers)
            properties.Headers = new Dictionary<string, object>(headers);

        // mandatory: an unroutable publish (lane queue missing) comes back as a return and the
        // confirm still succeeds, so declare the lane before forwarding — ExecuteAsync does.
        confirmChannel.BasicPublish(exchange: "", routingKey: MessagingTopology.TenantLaneQueue(tenantSlug),
            mandatory: true, basicProperties: properties, body: delivery.Body);
        confirmChannel.WaitForConfirmsOrDie(ConfirmTimeout);
    }
}
