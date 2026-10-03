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

    public static bool IsSensitive(string fieldName)
    {
        var normalized = fieldName.Replace("_", "").Replace("-", "").ToLowerInvariant();
        return ExactMatches.Contains(normalized) || ContainsMatches.Any(normalized.Contains);
    }

    public static JsonElement Mask(JsonElement data)
    {
        var node = JsonNode.Parse(data.GetRawText());
        MaskNode(node);
        return JsonSerializer.SerializeToElement(node);
    }

    private static void MaskNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (!IsSensitive(key))
                        MaskNode(obj[key]);
                    else if (obj[key] is not null)
                        obj[key] = MaskedValue; // a whole object/array under a sensitive name too
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    MaskNode(item);
                break;
        }
    }
}
