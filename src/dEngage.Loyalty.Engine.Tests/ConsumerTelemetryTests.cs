using App.Metrics;
using dEngage.Loyalty.Consumer;
using dEngage.Loyalty.Shared.Messaging;
using FluentAssertions;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// The Consumer's platform-style observability (dEngage style #2): Prometheus meters/timers and the
// debug-view section, fed by every delivery and every outbox batch.
public sealed class ConsumerTelemetryTests
{
    private readonly IMetricsRoot _metrics = new MetricsBuilder().Build();
    private readonly TestClock _clock = new();
    private readonly ConsumerTelemetry _telemetry;

    public ConsumerTelemetryTests() => _telemetry = new ConsumerTelemetry(_metrics, _clock);

    private static DeliveryRecord Delivery(DeliveryOutcome outcome, string queue = "q.loyalty", string? tenant = "fintech",
        Exception? error = null) =>
        new(queue, outcome, "order.created", tenant, "e-1", Redelivered: false, TimeSpan.FromMilliseconds(12), error);

    private long MeterCount(string name, params (string Key, string Value)[] tags) =>
        _metrics.Snapshot.Get().Contexts.SelectMany(c => c.Meters)
            .Where(m => m.MultidimensionalName == name
                        && tags.All(t => m.Tags.Keys.Zip(m.Tags.Values).Contains((t.Key, t.Value))))
            .Sum(m => m.Value.Count);

    [Fact]
    public void Every_outcome_is_counted_by_outcome_event_type_and_tenant()
    {
        _telemetry.Record(Delivery(DeliveryOutcome.Processed));
        _telemetry.Record(Delivery(DeliveryOutcome.Processed, tenant: "starbucks"));
        _telemetry.Record(Delivery(DeliveryOutcome.Duplicate));
        _telemetry.Record(Delivery(DeliveryOutcome.MovedToLane));

        MeterCount("loyalty_consumer_events", ("outcome", "processed")).Should().Be(2);
        MeterCount("loyalty_consumer_events", ("outcome", "processed"), ("tenant", "starbucks")).Should().Be(1);
        MeterCount("loyalty_consumer_events", ("outcome", "duplicate"), ("event_type", "order.created")).Should().Be(1);
        MeterCount("loyalty_consumer_events", ("outcome", "moved_to_lane")).Should().Be(1);

        var timer = _metrics.Snapshot.Get().Contexts.SelectMany(c => c.Timers)
            .Single(t => t.MultidimensionalName == "loyalty_consumer_processing");
        timer.Value.Rate.Count.Should().Be(2, "only deliveries that ran the engine are timed");
    }

    [Fact]
    public void The_snapshot_keeps_per_queue_activity_and_the_last_error()
    {
        _telemetry.Record(Delivery(DeliveryOutcome.Processed));
        _clock.Advance(TimeSpan.FromMinutes(1));
        _telemetry.Record(Delivery(DeliveryOutcome.Failed, queue: "q.loyalty.tenant.fintech",
            error: new InvalidOperationException("insufficient_balance: not enough points")));
        _telemetry.Record(Delivery(DeliveryOutcome.Unreadable, tenant: null));

        var snapshot = _telemetry.Snapshot();

        snapshot.Total.Should().Be(3);
        snapshot.Errors.Should().Be(2);
        snapshot.Queues["q.loyalty"].Deliveries.Should().Be(2);
        snapshot.Queues["q.loyalty.tenant.fintech"].LastOutcome.Should().Be("Failed");
        snapshot.LastError!.Queue.Should().Be("q.loyalty");
        snapshot.LastError.Tenant.Should().BeNull();
        MeterCount("loyalty_consumer_events", ("outcome", "unreadable"), ("tenant", "unknown")).Should().Be(1);
    }

    [Fact]
    public void Outbox_batches_are_counted()
    {
        _telemetry.RecordOutboxPublished(["points.earned", "points.earned", "tier.changed"]);
        _telemetry.RecordOutboxFailure(retried: 3, failed: 1, new TimeoutException("no confirm"));

        MeterCount("loyalty_outbox_events", ("outcome", "published"), ("event_type", "points.earned")).Should().Be(2);
        MeterCount("loyalty_outbox_events", ("outcome", "retry")).Should().Be(3);
        MeterCount("loyalty_outbox_events", ("outcome", "failed")).Should().Be(1);
        var outbox = _telemetry.Snapshot().Outbox;
        outbox.Published.Should().Be(3);
        outbox.LastPublishedAt.Should().Be(_clock.GetUtcNow());
        outbox.LastError!.ExceptionType.Should().Be(nameof(TimeoutException));
    }

    [Fact]
    public void The_debug_view_section_shows_counts_and_never_the_broker_password()
    {
        _telemetry.Record(Delivery(DeliveryOutcome.Processed));
        _telemetry.Record(Delivery(DeliveryOutcome.Failed, error: new InvalidOperationException("boom")));
        var config = new ConsumerConfig
        {
            Broker = new BrokerConnectionSettings { Host = "mq", User = "loyalty", Password = "s3cret-pw" },
            IsolatedTenants = ["fintech"]
        };

        var section = new ConsumerDebugViewSectionProvider(_telemetry, config).GetSection();

        section.SectionName.Name.Should().Be("Loyalty consumer");
        section.Badges.Select(b => b.Value).Should().StartWith(["2", "1"]);
        section.Value.Should().Contain("q.loyalty").And.Contain("fintech").And.Contain("boom");
        section.Value.Should().NotContain("s3cret-pw");
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
