using dEngage.Loyalty.Engine.Framework.Consumers;
using dEngage.Loyalty.Shared.Events;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Consumer.Handlers;

// Generic events have no transactional side effects of their own — the shared
// campaign evaluation (CampaignEvaluationService) does the actual work.
// EventType is null: this is the fallback IEventHandlerRegistry resolves to for
// anything not otherwise registered (today's DispatchAsync `default:` case).
public class GenericEventHandler(ILogger<GenericEventHandler> logger) : IEventHandler
{
    public string? EventType => null;

    public Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        var contactKey = envelope.Data.TryGetProperty("contact_key", out var ck) ? ck.GetString() : null;
        logger.LogInformation("GenericEvent: {EventType} [{EventId}] contact={ContactKey}",
            envelope.EventType, envelope.EventId, contactKey);
        return Task.CompletedTask;
    }
}
