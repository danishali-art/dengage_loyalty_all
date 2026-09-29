using System.Text.Json;

namespace dEngage.Loyalty.Api.Framework.Json;

// Single shared System.Text.Json convention (camelCase) for every response body — Nancy's own
// content-negotiation/serializer subsystem is intentionally bypassed in favor of writing JSON
// directly, so there is exactly one place JSON options are defined.
public static class JsonConventions
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new DecimalStringJsonConverter() }
    };

    // The event envelope's Data payload is consumed by dEngage.Loyalty.Consumer's handlers
    // (CampaignEvaluationService, CashAddedHandler, PointsRedeemHandler, ...), which all read
    // snake_case fields (contact_key, account_type_id, points_amount, ...) — a pre-existing
    // contract independent of this REST API's own camelCase convention above. Confirmed
    // empirically: using JsonConventions.Options here produced "contactKey", which every
    // handler's `data.GetProperty("contact_key")` silently failed to find (TryGetProperty
    // checks, not throws), so events were accepted and marked processed but never matched
    // anything — no error, no ledger entry, nothing.
    public static readonly JsonSerializerOptions EventDataOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new DecimalStringJsonConverter() }
    };
}
