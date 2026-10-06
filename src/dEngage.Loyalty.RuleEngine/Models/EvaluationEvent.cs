using System.Globalization;
using System.Text.Json;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.RuleEngine.Models;

public class EvaluationEvent
{
    public string EventType { get; init; } = default!;
    public string ContactKey { get; init; } = default!;
    public decimal Amount { get; init; }
    public string? Channel { get; init; }
    public DateTime OccurredAt { get; init; }
    public JsonElement Data { get; init; }

    // Caller guarantees data.contact_key exists (built-in contracts + GenericEventValidator).
    public static EvaluationEvent FromEnvelope(EventEnvelope envelope)
    {
        var data = envelope.Data;

        // CR 2026-10-05: burn events carry their amount as points_amount, so conditions and
        // min_event_amount on redeem/transfer rules see the real value. Everything else (and a
        // burn event without points_amount) keeps reading "amount".
        var amount = 0m;
        var amountField = EventTypes.Describe(envelope.EventType)?.Category == EventCategory.Burn
                          && data.TryGetProperty("points_amount", out _)
            ? "points_amount"
            : "amount";
        if (data.TryGetProperty(amountField, out var am))
        {
            if (am.ValueKind == JsonValueKind.Number) am.TryGetDecimal(out amount);
            else if (am.ValueKind == JsonValueKind.String)
                decimal.TryParse(am.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        }

        return new EvaluationEvent
        {
            EventType = envelope.EventType,
            ContactKey = data.GetProperty("contact_key").GetString()!,
            Amount = amount,
            Channel = data.TryGetProperty("channel", out var ch) && ch.ValueKind == JsonValueKind.String
                ? ch.GetString() : null,
            OccurredAt = envelope.OccurredAt == default
                ? DateTime.UtcNow
                : envelope.OccurredAt.ToUniversalTime(),
            Data = data
        };
    }
}
