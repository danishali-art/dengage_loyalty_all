namespace dEngage.Loyalty.Contract;

// RabbitMQ topology names, previously local consts inside EventConsumerWorker.ExecuteAsync.
// Centralized so any future consumer of this topology (not just the Consumer project) has
// one source of truth instead of retyping the strings.
public static class MessagingTopology
{
    public const string Exchange = "loyalty.events";
    public const string Queue = "q.loyalty";
    public const string DeadLetterQueue = "q.loyalty.dlq";
    public const string DeadLetterExchange = "loyalty.events.dlx";
    public const string OutboundExchange = "loyalty.outbound";

    // Platform alignment D12: an isolated tenant's events are moved from q.loyalty to their own
    // queue and processed there, so a burst from that tenant no longer delays everyone else.
    public const string TenantLaneQueuePrefix = "q.loyalty.tenant.";

    public static string TenantLaneQueue(string tenantSlug) => TenantLaneQueuePrefix + tenantSlug;
}
