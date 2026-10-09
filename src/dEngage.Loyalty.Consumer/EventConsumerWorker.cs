using System.Diagnostics;
using System.Text;
using System.Text.Json;
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

namespace dEngage.Loyalty.Consumer;

public class EventConsumerWorker(
    IServiceScopeFactory scopeFactory,
    RuleSyncService ruleSync,
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

        var factory = new ConnectionFactory
        {
            HostName = config.RabbitMqHost,
            Port = config.RabbitMqPort,
            UserName = config.RabbitMqUser,
            Password = config.RabbitMqPassword,
            DispatchConsumersAsync = true
        };

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

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                var envelope = JsonSerializer.Deserialize<EventEnvelope>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (envelope is null)
                {
                    channel.BasicNack(ea.DeliveryTag, false, false);
                    return;
                }

                await ProcessAsync(envelope, ct);
                channel.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Consumer: failed to process message");
                channel.BasicNack(ea.DeliveryTag, false, false);
            }
        };

        channel.BasicConsume(Queue, autoAck: false, consumer: consumer);
        logger.LogInformation("Consumer: listening on {Queue}", Queue);

        await Task.Delay(Timeout.Infinite, ct);
    }

    // internal for Engine.Tests (one engine run per delivery, CR 2026-10-06 D18 / R15).
    internal async Task ProcessAsync(EventEnvelope envelope, CancellationToken ct)
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
                return;
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

        var sw = Stopwatch.StartNew();
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

            sw.Stop();
            logger.LogInformation("Consumer: ✓ {EventType} [{EventId}] {Ms}ms",
                envelope.EventType,
                envelope.EventId.Length > 8 ? envelope.EventId[..8] : envelope.EventId,
                sw.ElapsedMilliseconds);
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
