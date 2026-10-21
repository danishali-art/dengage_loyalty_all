using App.Metrics;
using App.Metrics.Meter;
using App.Metrics.Timer;
using dEngage.Common.Utilities.DebugView;
using dEngage.Common.Utilities.DebugView.Models;

namespace dEngage.Loyalty.Consumer;

// What happened to one delivery from q.loyalty or a tenant lane.
public enum DeliveryOutcome
{
    Processed,
    // Already processed (a redelivery): acked without running anything.
    Duplicate,
    // An isolated tenant's event, moved to its lane (D12) and processed there.
    MovedToLane,
    // Processing threw: nacked into the DLQ.
    Failed,
    // Not a readable event envelope: nacked into the DLQ.
    Unreadable
}

public sealed record DeliveryRecord(
    string Queue,
    DeliveryOutcome Outcome,
    string? EventType,
    string? Tenant,
    string? EventId,
    bool Redelivered,
    TimeSpan Duration,
    Exception? Error = null);

// Observability for the Consumer, the way the platform's message consumers have it (platform
// alignment, "dEngage style" #2): Prometheus meters and timers through App.Metrics (exported when
// appsettings.metrics.json enables them) and a debug-view section. The per-delivery log event is
// written by EventConsumerWorker.
//
// Metric tags carry the outcome, event type and tenant slug — never contact keys or event data.
public sealed class ConsumerTelemetry(IMetrics metrics, TimeProvider clock)
{
    internal static readonly MeterOptions EventsMeter = new()
    {
        Name = "loyalty_consumer_events",
        MeasurementUnit = Unit.Events
    };

    internal static readonly TimerOptions ProcessingTimer = new()
    {
        Name = "loyalty_consumer_processing",
        MeasurementUnit = Unit.Events,
        DurationUnit = TimeUnit.Milliseconds,
        RateUnit = TimeUnit.Seconds
    };

    internal static readonly MeterOptions OutboxMeter = new()
    {
        Name = "loyalty_outbox_events",
        MeasurementUnit = Unit.Events
    };

    private readonly object _lock = new();
    private readonly Dictionary<DeliveryOutcome, long> _outcomes = Enum.GetValues<DeliveryOutcome>().ToDictionary(o => o, _ => 0L);
    private readonly Dictionary<string, QueueSummary> _queues = new(StringComparer.Ordinal);
    private long _outboxPublished, _outboxRetried, _outboxFailed;
    private DateTimeOffset? _outboxLastPublishedAt;
    private ErrorSummary? _lastError, _lastOutboxError;

    public void Record(DeliveryRecord delivery)
    {
        var eventType = delivery.EventType ?? "unknown";
        metrics.Measure.Meter.Mark(EventsMeter, new MetricTags(
            ["outcome", "event_type", "tenant"],
            [Tag(delivery.Outcome), eventType, delivery.Tenant ?? "unknown"]));

        // Only deliveries that ran the engine say something about processing time.
        if (delivery.Outcome is DeliveryOutcome.Processed or DeliveryOutcome.Failed)
            metrics.Provider.Timer.Instance(ProcessingTimer, new MetricTags("event_type", eventType))
                .Record((long)delivery.Duration.TotalMilliseconds, TimeUnit.Milliseconds);

        var now = clock.GetUtcNow();
        lock (_lock)
        {
            _outcomes[delivery.Outcome]++;
            if (!_queues.TryGetValue(delivery.Queue, out var queue))
                _queues[delivery.Queue] = queue = new QueueSummary();
            queue.Deliveries++;
            queue.LastDeliveryAt = now;
            queue.LastOutcome = delivery.Outcome.ToString();
            queue.LastDurationMs = (long)delivery.Duration.TotalMilliseconds;

            if (delivery.Outcome is DeliveryOutcome.Failed or DeliveryOutcome.Unreadable)
                _lastError = new ErrorSummary(now, delivery.Queue, delivery.EventType, delivery.EventId, delivery.Tenant,
                    delivery.Error?.GetType().Name, delivery.Error?.Message);
        }
    }

    public void RecordOutboxPublished(IEnumerable<string> eventTypes)
    {
        var count = 0;
        foreach (var eventType in eventTypes)
        {
            metrics.Measure.Meter.Mark(OutboxMeter, new MetricTags(["outcome", "event_type"], ["published", eventType]));
            count++;
        }

        lock (_lock)
        {
            _outboxPublished += count;
            _outboxLastPublishedAt = clock.GetUtcNow();
        }
    }

    // A batch the broker didn't confirm: `retried` rows go back to the outbox with a backoff,
    // `failed` ones reached the attempt limit.
    public void RecordOutboxFailure(int retried, int failed, Exception error)
    {
        if (retried > 0)
            metrics.Measure.Meter.Mark(OutboxMeter, new MetricTags("outcome", "retry"), retried);
        if (failed > 0)
            metrics.Measure.Meter.Mark(OutboxMeter, new MetricTags("outcome", "failed"), failed);

        lock (_lock)
        {
            _outboxRetried += retried;
            _outboxFailed += failed;
            _lastOutboxError = new ErrorSummary(clock.GetUtcNow(), MessagingTopologyOutbound, null, null, null,
                error.GetType().Name, error.Message);
        }
    }

    public ConsumerTelemetrySnapshot Snapshot()
    {
        lock (_lock)
        {
            return new ConsumerTelemetrySnapshot(
                _outcomes.ToDictionary(p => p.Key.ToString(), p => p.Value),
                _queues.ToDictionary(p => p.Key, p => p.Value with { }),
                _lastError,
                new OutboxSummary(_outboxPublished, _outboxRetried, _outboxFailed, _outboxLastPublishedAt, _lastOutboxError));
        }
    }

    private const string MessagingTopologyOutbound = dEngage.Loyalty.Contract.MessagingTopology.OutboundExchange;

    private static string Tag(DeliveryOutcome outcome) => outcome switch
    {
        DeliveryOutcome.Processed => "processed",
        DeliveryOutcome.Duplicate => "duplicate",
        DeliveryOutcome.MovedToLane => "moved_to_lane",
        DeliveryOutcome.Failed => "failed",
        _ => "unreadable"
    };
}

public sealed record QueueSummary
{
    public long Deliveries { get; set; }
    public DateTimeOffset? LastDeliveryAt { get; set; }
    public string? LastOutcome { get; set; }
    public long LastDurationMs { get; set; }
}

public sealed record ErrorSummary(
    DateTimeOffset At, string Queue, string? EventType, string? EventId, string? Tenant, string? ExceptionType, string? Message);

public sealed record OutboxSummary(long Published, long Retried, long Failed, DateTimeOffset? LastPublishedAt, ErrorSummary? LastError);

public sealed record ConsumerTelemetrySnapshot(
    IReadOnlyDictionary<string, long> Outcomes,
    IReadOnlyDictionary<string, QueueSummary> Queues,
    ErrorSummary? LastError,
    OutboxSummary Outbox)
{
    public long Total => Outcomes.Values.Sum();
    public long Errors => Outcomes[nameof(DeliveryOutcome.Failed)] + Outcomes[nameof(DeliveryOutcome.Unreadable)];
}

// "Loyalty consumer" on the platform's debug-view page (the Consumer is cluster-internal, so the
// platform's internal endpoints stay on there; see .claude/rules/backend.md).
public sealed class ConsumerDebugViewSectionProvider(ConsumerTelemetry telemetry, ConsumerConfig config) : IDebugViewSectionProvider
{
    public IDebugViewSection GetSection()
    {
        var snapshot = telemetry.Snapshot();
        return new ConsumerDebugViewSection(snapshot, new
        {
            snapshot.Outcomes,
            snapshot.Queues,
            snapshot.LastError,
            snapshot.Outbox,
            IsolatedTenants = config.IsolatedTenants,
            config.GenericEventTypes,
            Broker = new { config.Broker.Source, config.Broker.Host, config.Broker.Port, config.Broker.VirtualHost, config.Broker.UseSsl }
        });
    }

    private sealed class ConsumerDebugViewSection : DebugViewSection
    {
        public ConsumerDebugViewSection(ConsumerTelemetrySnapshot snapshot, object value)
            : base(new SectionName("Loyalty consumer"), "loyalty-consumer", value)
        {
            Badges = BadgeCollection.GetCountBadges(snapshot.Total, snapshot.Total - snapshot.Errors, snapshot.Errors).Badges;
        }
    }
}
