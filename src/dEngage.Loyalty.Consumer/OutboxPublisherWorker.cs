using System.Text;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace dEngage.Loyalty.Consumer;

public class OutboxPublisherWorker(
    IServiceProvider serviceProvider,
    ConsumerConfig config,
    ILogger<OutboxPublisherWorker> logger) : BackgroundService
{
    private const string Exchange = MessagingTopology.OutboundExchange;
    private const int BatchSize = 100;
    private const int MaxAttempts = 8;

    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(12)
    ];

    private DateOnly _lastPurgeDate = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPublishLoopAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox publisher error — reconnecting in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RunPublishLoopAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = config.RabbitMqHost,
            Port = config.RabbitMqPort,
            UserName = config.RabbitMqUser,
            Password = config.RabbitMqPassword
        };

        using var connection = factory.CreateConnection("loyalty-outbox-publisher");
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare(Exchange, ExchangeType.Topic, durable: true);
        channel.ConfirmSelect();

        logger.LogInformation("Outbox publisher ready — exchange: {Exchange}", Exchange);

        while (!ct.IsCancellationRequested)
        {
            await PurgeIfDueAsync(ct);

            var published = await PublishBatchAsync(channel, ct);
            if (published == 0)
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    // Daily cleanup: published rows are deleted after 30 days, keeping the live set small.
    // Even if multiple instances purge on the same day, the DELETE is idempotent.
    private async Task PurgeIfDueAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today == _lastPurgeDate) return;

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();

        var deleted = await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM outbox_events WHERE status = 'published' AND published_at < now() - interval '30 days'",
            ct);

        _lastPurgeDate = today;

        if (deleted > 0)
            logger.LogInformation("Outbox purge: deleted {Count} published rows older than 30 days", deleted);
    }

    private async Task<int> PublishBatchAsync(IModel channel, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();

        // SKIP LOCKED: keeps multiple Consumer instances from grabbing the same row.
        // The lock is held for the whole tx; the publish outcome (published/backoff) is written in the same tx.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var batch = await db.OutboxEvents
            .FromSql($"""
                SELECT * FROM outbox_events
                WHERE status = 'pending' AND next_attempt_at <= now()
                ORDER BY id
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        if (batch.Count == 0)
        {
            await tx.CommitAsync(ct);
            return 0;
        }

        var tenantSlugResolver = scope.ServiceProvider.GetRequiredService<ITenantSlugResolver>();

        Exception? publishError = null;
        try
        {
            foreach (var evt in batch)
            {
                var props = channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";
                props.MessageId = evt.EventId.ToString();

                // Routing key is external contract — the tenant slug, not the internal Guid.
                var tenantSlug = await tenantSlugResolver.ResolveSlugAsync(evt.TenantId, ct);
                channel.BasicPublish(
                    exchange: Exchange,
                    routingKey: $"{tenantSlug}.{evt.EventType}",
                    basicProperties: props,
                    body: Encoding.UTF8.GetBytes(evt.Payload));
            }

            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(10));

            var now = DateTime.UtcNow;
            foreach (var evt in batch)
            {
                evt.Status = OutboxStatus.Published;
                evt.PublishedAt = now;
            }
        }
        catch (Exception ex)
        {
            // No confirm received → unknown which messages reached the broker.
            // All of them fall back to retry; at-least-once, the consumer dedups by eventId.
            publishError = ex;
            var now = DateTime.UtcNow;
            foreach (var evt in batch)
            {
                evt.Attempts += 1;
                if (evt.Attempts >= MaxAttempts)
                {
                    evt.Status = OutboxStatus.Failed;
                    logger.LogError("Outbox event permanently failed: id={Id} type={Type} tenant={Tenant}",
                        evt.Id, evt.EventType, evt.TenantId);
                }
                else
                {
                    evt.NextAttemptAt = now + Backoff[Math.Min(evt.Attempts - 1, Backoff.Length - 1)];
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (publishError is not null)
        {
            // The channel may have closed on a confirm failure — let the outer loop reconnect
            throw new InvalidOperationException("Outbox publish/confirm failed", publishError);
        }

        logger.LogInformation("Outbox: {Count} events published", batch.Count);
        return batch.Count;
    }
}
