using System.Globalization;
using System.Text.Json;

namespace dEngage.Loyalty.RuleEngine;

// Shared numeric/string comparison primitives used by both the flat ConditionEvaluator (Streak)
// and the grouped GroupedConditionEvaluator (Rules/Card Buckets) — kept as one place so the two
// DSLs never silently diverge on what "equal" or "greater than" means for a JSON scalar.
internal static class JsonValueComparison
{
    // Numeric coercion first ("007" == 7, "1.0" == 1), then bool-vs-bool,
    // then case-insensitive string. Cross-kind otherwise never equal.
    public static bool ValueEquals(JsonElement actual, JsonElement expected)
    {
        if (TryGetDecimal(actual, out var da) && TryGetDecimal(expected, out var de))
            return da == de;

        if (actual.ValueKind is JsonValueKind.True or JsonValueKind.False ||
            expected.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return actual.ValueKind == expected.ValueKind;

        if (actual.ValueKind == JsonValueKind.String && expected.ValueKind == JsonValueKind.String)
            return string.Equals(actual.GetString(), expected.GetString(), StringComparison.OrdinalIgnoreCase);

        return false;
    }

    public static int? Compare(JsonElement actual, JsonElement expected)
    {
        if (TryGetDecimal(actual, out var da) && TryGetDecimal(expected, out var de))
            return da.CompareTo(de);
        return null;
    }

    public static bool StartsWith(JsonElement actual, JsonElement expected) =>
        actual.ValueKind == JsonValueKind.String && expected.ValueKind == JsonValueKind.String &&
        (actual.GetString() ?? "").StartsWith(expected.GetString() ?? "", StringComparison.OrdinalIgnoreCase);

    public static bool TryGetDecimal(JsonElement el, out decimal value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number)
            return el.TryGetDecimal(out value);
        if (el.ValueKind == JsonValueKind.String)
            return decimal.TryParse(el.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        return false;
    }
}
