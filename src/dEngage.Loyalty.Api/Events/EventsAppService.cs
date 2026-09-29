using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Api.Framework.Messaging;
using dEngage.Loyalty.Api.Framework.Options;
using dEngage.Loyalty.Api.Framework.RateLimiting;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace dEngage.Loyalty.Api.Events;

public interface IEventsAppService
{
    Task<EventAcceptedResponse> PublishAsync<TData>(string tenantId, string eventType, TData data, string? idempotencyKey, CancellationToken ct);
    Task<EventStatusResponse> GetStatusAsync(string tenantId, string eventId, CancellationToken ct);
    EventTypesResponse GetEventTypes();
}

// Thin HTTP -> RabbitMQ translator (plan §7/§4) — never resolves the business outcome itself,
// only that the event was accepted for processing (202, never 200: see EventsModule).
public sealed class EventsAppService(
    IEventPublisher publisher,
    LoyaltyDbContext db,
    IRateLimiter rateLimiter,
    IOptions<RateLimitOptions> rateLimitOptions,
    IOptions<RabbitMqOptions> rabbitMqOptions) : IEventsAppService
{
    public async Task<EventAcceptedResponse> PublishAsync<TData>(
        string tenantId, string eventType, TData data, string? idempotencyKey, CancellationToken ct)
    {
        if (!await rateLimiter.TryAcquireAsync("events", tenantId, rateLimitOptions.Value.EventIngestionPerMinutePerTenant, ct))
            throw new TooManyRequestsApiException($"Ingestion rate limit exceeded for tenant '{tenantId}'.");

        if (!EventTypes.IsBuiltIn(eventType) && !rabbitMqOptions.Value.GenericEventTypes.Contains(eventType))
        {
            throw new ValidationApiException(
                $"Event type '{eventType}' is not a built-in type and is not configured in RabbitMq:GenericEventTypes for this deployment.");
        }

        // CR-01: Scheduled-source events (birthdaybonus, points.expired) are synthesized
        // internally on a deterministic key — accepting them from a caller would let a bad
        // actor spoof or duplicate an engine-owned trigger (A10 guarantee #10).
        if (!EventTypes.IsExternallyPublishable(eventType))
        {
            throw new ValidationApiException(
                $"Event type '{eventType}' is scheduled internally and cannot be published via the ingestion API.");
        }

        var envelope = EventEnvelopeFactory.Create(eventType, tenantId, data, idempotencyKey, null);
        await publisher.PublishAsync(envelope, ct);
        return new EventAcceptedResponse(envelope.EventId, "accepted");
    }

    public async Task<EventStatusResponse> GetStatusAsync(string tenantId, string eventId, CancellationToken ct)
    {
        var inbox = await db.EventInbox.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.EventId == eventId, ct)
            ?? throw new NotFoundApiException($"Event '{eventId}'");

        return new EventStatusResponse(inbox.EventId, inbox.EventType, inbox.Status, inbox.ReceivedAt, inbox.ProcessedAt, inbox.Error);
    }

    // Deployment-wide, not tenant-scoped — see RabbitMqOptions.GenericEventTypes: every tenant
    // shares the same allow-list of non-built-in event types this deployment accepts at all.
    // Which tenant's Rules actually react to one is a separate, already-tenant-scoped concern
    // (RuleEngine matches rules by tenantId + programId, not by this list).
    public EventTypesResponse GetEventTypes() =>
        new(EventTypes.All, rabbitMqOptions.Value.GenericEventTypes);
}
