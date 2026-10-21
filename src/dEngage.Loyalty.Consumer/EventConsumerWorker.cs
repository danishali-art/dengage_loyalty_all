using System.Diagnostics;
using System.Text;
using System.Text.Json;
using dEngage.Common.Utilities.LogEvent;
using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using dEngage.Loyalty.Contract;

namespace dEngage.Loyalty.Consumer;

public class EventConsumerWorker(
    IServiceScopeFactory scopeFactory,
    RuleSyncService ruleSync,
    ConsumerTelemetry telemetry,
    ILogger<EventConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait until RuleSync is ready. If the initial load fails, an exception
        // is thrown and host shutdown begins — exit quietly.
        try
        {
            await ruleSync.WaitUntilReadyAsync().WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Consumer: RuleSync failed to become ready, aborting");
            return;
        }

        logger.LogInformation("Consumer: rules ready, starting RabbitMQ listener");

        using var scope = scopeFactory.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<ConsumerConfig>();

        var factory = config.CreateConnectionFactory(dispatchConsumersAsync: true);

        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        const string Queue = MessagingTopology.Queue;
        const string Dlq   = MessagingTopology.DeadLetterQueue;
        const string Dlx   = MessagingTopology.DeadLetterExchange;

        // Single queue — all event types are processed in order, per-customer ordering guaranteed
        channel.ExchangeDeclare(MessagingTopology.Exchange, ExchangeType.Direct, durable: true);
        channel.ExchangeDeclare(Dlx, ExchangeType.Fanout, durable: true);

        // Nacked messages land in the DLQ via the DLX — nothing is lost.
        // NOTE: if q.loyalty was previously declared without arguments, RabbitMQ
        // throws PRECONDITION_FAILED; the existing queue must be deleted once.
        channel.QueueDeclare(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object> { ["x-dead-letter-exchange"] = Dlx });
        channel.QueueDeclare(Dlq, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(Dlq, Dlx, routingKey: "");

        // Bind every event type to the same queue. Direct exchange: generic types
        // not listed in RabbitMq:GenericEventTypes are dropped silently by RabbitMQ.
        foreach (var rk in EventTypes.All.Concat(config.GenericEventTypes).Distinct())
            channel.QueueBind(Queue, MessagingTopology.Exchange, rk);

        channel.BasicQos(0, 1, false); // sequential processing

        // D12 tenant lanes (see TenantLanes). Each isolated tenant gets its own channel (a channel
        // isn't thread-safe), queue and sequential worker. Lanes are declared and consuming before
        // q.loyalty starts, so a forwarded event always has a queue to land in.
        var isolatedTenants = new HashSet<string>(config.IsolatedTenants, StringComparer.Ordinal);
        using var forwardChannel = connection.CreateModel();
        forwardChannel.ConfirmSelect();
        var laneChannels = new List<IModel>();
        try
        {
            foreach (var tenant in isolatedTenants)
            {
                var laneChannel = connection.CreateModel();
                laneChannels.Add(laneChannel);
                var laneQueue = TenantLanes.Declare(laneChannel, tenant);
                laneChannel.BasicQos(0, 1, false); // sequential within the tenant
                var laneConsumer = new AsyncEventingBasicConsumer(laneChannel);
                laneConsumer.Received += (_, ea) => HandleDeliveryAsync(laneChannel, laneQueue, ea, forward: null, ct);
                laneChannel.BasicConsume(laneQueue, autoAck: false, consumer: laneConsumer);
                logger.LogInformation("Consumer: tenant {Tenant} is isolated, listening on {Queue}", tenant, laneQueue);
            }

            var forward = isolatedTenants.Count > 0 ? (isolatedTenants, forwardChannel) : default((HashSet<string>, IModel)?);
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += (_, ea) => HandleDeliveryAsync(channel, Queue, ea, forward, ct);

            channel.BasicConsume(Queue, autoAck: false, consumer: consumer);
            logger.LogInformation("Consumer: listening on {Queue}", Queue);

            await Task.Delay(Timeout.Infinite, ct);
        }
        finally
        {
            foreach (var laneChannel in laneChannels)
                laneChannel.Dispose();
        }
    }

    // One delivery from q.loyalty or a tenant lane. `forward` is set only for q.loyalty: events of
    // an isolated tenant are moved to that tenant's queue instead of being processed here.
    // Every delivery ends in one log event and one ConsumerTelemetry record, whatever happens to it.
    private async Task HandleDeliveryAsync(IModel channel, string queue, BasicDeliverEventArgs ea,
        (HashSet<string> Tenants, IModel ConfirmChannel)? forward, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        EventEnvelope? envelope = null;
        DeliveryOutcome outcome;
        Exception? error = null;
        try
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                envelope = JsonSerializer.Deserialize<EventEnvelope>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                error = ex;
            }

            if (envelope is null)
            {
                channel.BasicNack(ea.DeliveryTag, false, false);
                outcome = DeliveryOutcome.Unreadable;
            }
            else if (forward is { } lane && lane.Tenants.Contains(envelope.Tenant))
            {
                // Ack only after the broker confirmed the copy (TenantLanes.Forward).
                TenantLanes.Forward(lane.ConfirmChannel, ea, envelope.Tenant);
                channel.BasicAck(ea.DeliveryTag, false);
                outcome = DeliveryOutcome.MovedToLane;
            }
            else
            {
                outcome = await ProcessAsync(envelope, ct);
                channel.BasicAck(ea.DeliveryTag, false);
            }
        }
        catch (Exception ex)
        {
            error = ex;
            outcome = DeliveryOutcome.Failed;
            channel.BasicNack(ea.DeliveryTag, false, false);
        }

        var delivery = new DeliveryRecord(queue, outcome, envelope?.EventType, envelope?.Tenant, envelope?.EventId,
            ea.Redelivered, Stopwatch.GetElapsedTime(started), error);
        telemetry.Record(delivery);
        WriteLogEvent(delivery);
    }

    // The platform's log-event shape (its message consumers write one per message), so Graylog/Seq
    // dashboards and alerts work the same for loyalty. Deliberately without the event data: it can
    // carry customer details, and the inbox keeps the payload anyway.
    private void WriteLogEvent(DeliveryRecord delivery)
    {
        using var scope = scopeFactory.CreateScope();
        var logEvent = scope.ServiceProvider.GetService<ILogEvent>();
        if (logEvent is null)
        {
            // A host without the platform utilities (some tests): a plain log line instead.
            logger.Log(delivery.Error is null ? LogLevel.Information : LogLevel.Error, delivery.Error,
                "Consumer: {Outcome} {EventType} [{EventId}] on {Queue} in {Ms}ms",
                delivery.Outcome, delivery.EventType, delivery.EventId, delivery.Queue, (long)delivery.Duration.TotalMilliseconds);
            return;
        }

        logEvent.AddOrUpdate("consumerName", nameof(EventConsumerWorker));
        logEvent.AddOrUpdate("queueAddress", delivery.Queue);
        logEvent.AddOrUpdate("messageName", delivery.EventType ?? "unknown");
        logEvent.AddOrUpdate("messageType", delivery.EventType ?? "unknown");
        logEvent.AddOrUpdate("messageId", delivery.EventId ?? "unknown");
        logEvent.AddOrUpdate("tenant", delivery.Tenant ?? "unknown");
        logEvent.AddOrUpdate("outcome", delivery.Outcome.ToString());
        logEvent.AddOrUpdate("messageRedelivered", delivery.Redelivered);
        logEvent.AddOrUpdate("timeSpentOnConsumer", delivery.Duration);

        if (delivery.Error is not null)
        {
            logEvent.AddException(delivery.Error);
            logger.LogError("{@EventData}", logEvent.Data);
        }
        else
        {
            logger.LogInformation("{@EventData}", logEvent.Data);
        }
    }

    // internal for Engine.Tests (one engine run per delivery, CR 2026-10-06 D18 / R15).
    internal async Task<DeliveryOutcome> ProcessAsync(EventEnvelope envelope, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();

        // Idempotency — write to event_inbox
        var existing = await db.EventInbox
            .FirstOrDefaultAsync(x => x.TenantId == envelope.Tenant && x.EventId == envelope.EventId, ct);

        if (existing is not null)
        {
            if (existing.Status == InboxStatus.Processed)
            {
                logger.LogDebug("Consumer: skipping duplicate {EventId}", envelope.EventId);
                return DeliveryOutcome.Duplicate;
            }
        }
        else
        {
            var inbox = new EventInbox
            {
                EventId = envelope.EventId,
                TenantId = envelope.Tenant,
                EventType = envelope.EventType,
                Payload = JsonSerializer.Serialize(envelope),
                ReceivedAt = DateTime.UtcNow,
                Status = InboxStatus.Pending,
                ContactKey = InboxContactKey(envelope)
            };
            db.EventInbox.Add(inbox);
            await db.SaveChangesAsync(ct);
        }

        try
        {
            if (!EventTypes.IsBuiltIn(envelope.EventType))
                GenericEventValidator.Validate(envelope);

            await AppendEventLogAsync(db, envelope, ct);
            await DispatchAsync(envelope, scope, ct);

            // Campaign rules run for EVERY event type — here, or in the handler for the types
            // listed in HandlerEvaluatedEvents (calling again here would run the engine twice).
            if (!HandlerEvaluatedEvents.All.Contains(envelope.EventType))
                await scope.ServiceProvider.GetRequiredService<ICampaignEvaluationService>()
                    .EvaluateAsync(envelope, ct);

            await db.EventInbox
                .Where(x => x.TenantId == envelope.Tenant && x.EventId == envelope.EventId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, InboxStatus.Processed)
                    .SetProperty(x => x.ProcessedAt, DateTime.UtcNow), ct);

            return DeliveryOutcome.Processed;
        }
        catch (Exception ex)
        {
            await db.EventInbox
                .Where(x => x.TenantId == envelope.Tenant && x.EventId == envelope.EventId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, InboxStatus.Failed)
                    .SetProperty(x => x.Error, ex.Message), ct);
            throw;
        }
    }

    // Idempotent (ON CONFLICT DO NOTHING) history row for occurred_within window
    // queries. Written before dispatch — Faz 2 (streak) must NOT rely on this
    // insert's rowcount for its own idempotency.
    private static async Task AppendEventLogAsync(LoyaltyDbContext db, EventEnvelope envelope, CancellationToken ct)
    {
        var contactKey = ReadContactKey(envelope);
        if (contactKey is null)
            return; // only order.refunded among built-ins carries no contact_key

        var occurredAt = envelope.OccurredAt == default
            ? DateTime.UtcNow
            : envelope.OccurredAt.ToUniversalTime();

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO event_log (tenant_id, event_id, contact_key, event_type, occurred_at)
            VALUES ({envelope.Tenant}, {envelope.EventId}, {contactKey}, {envelope.EventType}, {occurredAt})
            ON CONFLICT (tenant_id, event_id) DO NOTHING", ct);
    }

    private static string? ReadContactKey(EventEnvelope envelope) =>
        envelope.Data.ValueKind == JsonValueKind.Object &&
        envelope.Data.TryGetProperty("contact_key", out var ck) &&
        ck.ValueKind == JsonValueKind.String &&
        !string.IsNullOrEmpty(ck.GetString())
            ? ck.GetString()
            : null;

    // CR 2026-10-02 (Customer 360): recorded on the inbox row so the customer view can list a
    // customer's events. A key longer than the column is left out rather than failing the
    // inbox insert — the event must still be deduplicated and processed.
    internal static string? InboxContactKey(EventEnvelope envelope) =>
        ReadContactKey(envelope) is { Length: <= 255 } contactKey ? contactKey : null;

    private static Task DispatchAsync(EventEnvelope envelope, IServiceScope scope, CancellationToken ct) =>
        scope.ServiceProvider.GetRequiredService<IEventHandlerRegistry>()
            .Resolve(envelope.EventType)
            .HandleAsync(envelope, ct);
}
