using System.Globalization;
using System.Text.Json;

namespace dEngage.Loyalty.Shared.Events;

// Contract for non-built-in (generic) events: envelope (event_id, event_type ≤50,
// tenant, occurred_at UTC) + data.contact_key + data.amount + data.channel are mandatory.
// Violations throw — the consumer marks the inbox row 'failed' and nacks to the DLQ.
public static class GenericEventValidator
{
    public static void Validate(EventEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.EventId))
            throw new InvalidOperationException("invalid_generic_event: event_id missing");
        if (string.IsNullOrWhiteSpace(envelope.Tenant))
            throw new InvalidOperationException("invalid_generic_event: tenant missing");
        if (string.IsNullOrWhiteSpace(envelope.EventType))
            throw new InvalidOperationException("invalid_generic_event: event_type missing");
        if (envelope.EventType.Length > 50)
            throw new InvalidOperationException("invalid_generic_event: event_type longer than 50 chars");
        if (envelope.OccurredAt == default)
            throw new InvalidOperationException("invalid_generic_event: occurred_at missing");
        if (envelope.OccurredAt.Kind == DateTimeKind.Unspecified)
            throw new InvalidOperationException("invalid_generic_event: occurred_at must be UTC ISO-8601 (use 'Z' or an offset)");

        if (envelope.Data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("invalid_generic_event: data object missing");

        RequireNonEmptyString(envelope.Data, "contact_key");
        RequireNonEmptyString(envelope.Data, "channel");
        RequireAmount(envelope.Data);
    }

    static void RequireNonEmptyString(JsonElement data, string field)
    {
        if (!data.TryGetProperty(field, out var el) ||
            el.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(el.GetString()))
            throw new InvalidOperationException($"invalid_generic_event: data.{field} missing or empty");
    }

    static void RequireAmount(JsonElement data)
    {
        if (!data.TryGetProperty("amount", out var el))
            throw new InvalidOperationException("invalid_generic_event: data.amount missing");

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out _))
            return;
        if (el.ValueKind == JsonValueKind.String &&
            decimal.TryParse(el.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            return;

        throw new InvalidOperationException("invalid_generic_event: data.amount must be a decimal (string or number)");
    }
}
