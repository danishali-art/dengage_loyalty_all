using System.Text.Json;
using System.Text.Json.Nodes;

namespace dEngage.Loyalty.Api.Customers;

// CR 2026-10-02 (Customer 360, D4/O2): the event drawer shows the event's data with sensitive
// values replaced. Matched by field name, at any depth, ignoring case and '_' / '-' so
// card_number, cardNumber and card-number are all caught:
//   - name contains phone, mobile or msisdn;
//   - name is card_number, pan, iban, national_id, iqama or passport.
// "pan" is an exact match only — a "contains" match would hit names like "company".
internal static class CustomerPayloadMasker
{
    public const string MaskedValue = "***";

    private static readonly string[] ContainsMatches = ["phone", "mobile", "msisdn"];
    private static readonly HashSet<string> ExactMatches = ["cardnumber", "pan", "iban", "nationalid", "iqama", "passport"];

    public static bool IsSensitive(string fieldName) => IsSensitive(fieldName, EmptyExtras);

    // Platform alignment D4: a tenant can name extra fields to mask (loyalty_config
    // loyalty.api/Pii.ExtraMaskedFields), e.g. a country-specific id. Exact matches, normalized
    // like the built-in names. Additive only — config can never un-mask a built-in field.
    public static bool IsSensitive(string fieldName, IReadOnlySet<string> extraExactMatches)
    {
        var normalized = Normalize(fieldName);
        return ExactMatches.Contains(normalized) || extraExactMatches.Contains(normalized)
            || ContainsMatches.Any(normalized.Contains);
    }

    public static JsonElement Mask(JsonElement data) => Mask(data, []);

    public static JsonElement Mask(JsonElement data, IEnumerable<string> extraMaskedFields)
    {
        var extras = extraMaskedFields.Select(Normalize).Where(n => n.Length > 0).ToHashSet();
        var node = JsonNode.Parse(data.GetRawText());
        MaskNode(node, extras);
        return JsonSerializer.SerializeToElement(node);
    }

    private static readonly IReadOnlySet<string> EmptyExtras = new HashSet<string>();

    private static string Normalize(string fieldName) =>
        fieldName.Replace("_", "").Replace("-", "").ToLowerInvariant();

    private static void MaskNode(JsonNode? node, IReadOnlySet<string> extras)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (!IsSensitive(key, extras))
                        MaskNode(obj[key], extras);
                    else if (obj[key] is not null)
                        obj[key] = MaskedValue; // a whole object/array under a sensitive name too
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    MaskNode(item, extras);
                break;
        }
    }
}
