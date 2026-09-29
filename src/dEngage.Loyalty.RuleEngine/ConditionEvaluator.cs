using System.Globalization;
using System.Text.Json;
using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine;

public static class ConditionEvaluator
{
    // Resolved from ConditionContext, not the event payload
    private const string TierField = "tier";

    // Resolved from evt.OccurredAt (always UTC), not the event payload — lets a rule/campaign
    // gate on time-of-day or day-of-week (e.g. Card Buckets' "time limitation" dimension) without
    // depending on the publisher having sent a precomputed field.
    private const string HourOfDayField = "event.hour_of_day";
    private const string DayOfWeekField = "event.day_of_week";
    private static readonly string[] DayNames =
        { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

    public static bool Evaluate(List<ConditionClause>? conditions, EvaluationEvent evt, ConditionContext context)
    {
        if (conditions is null || conditions.Count == 0) return true;

        foreach (var clause in conditions)
            if (!EvaluateClause(clause, evt, context))
                return false;

        return true;
    }

    private static bool EvaluateClause(ConditionClause clause, EvaluationEvent evt, ConditionContext context)
    {
        if (clause.Op == ConditionOps.OccurredWithin)
        {
            if (!OccurredWithinValue.TryParse(clause.Value, out var window)) return false;
            if (!context.LatestEventAt.TryGetValue(window.AfterEvent, out var lastAt)) return false;
            var elapsed = evt.OccurredAt - lastAt;
            return elapsed >= TimeSpan.Zero && elapsed <= TimeSpan.FromHours(window.Hours);
        }

        if (clause.Field == TierField)
            return EvaluateTier(clause, context.CustomerTierName);

        var values = clause.Field switch
        {
            HourOfDayField => new List<JsonElement> { JsonSerializer.SerializeToElement(evt.OccurredAt.Hour) },
            DayOfWeekField => new List<JsonElement> { JsonSerializer.SerializeToElement(DayNames[(int)evt.OccurredAt.DayOfWeek]) },
            _ => ResolvePath(evt.Data, clause.Field)
        };

        if (clause.Op == ConditionOps.Exists)
            return WantsExists(clause.Value) == values.Count > 0;

        if (values.Count == 0) return false;

        return clause.Op switch
        {
            ConditionOps.Eq  => values.Any(v => ValueEquals(v, clause.Value)),
            ConditionOps.Ne  => !values.Any(v => ValueEquals(v, clause.Value)),
            ConditionOps.In  => values.Any(v => InArray(v, clause.Value)),
            ConditionOps.Gte => values.Any(v => Compare(v, clause.Value) is >= 0),
            ConditionOps.Lte => values.Any(v => Compare(v, clause.Value) is <= 0),
            ConditionOps.Gt  => values.Any(v => Compare(v, clause.Value) is > 0),
            ConditionOps.Lt  => values.Any(v => Compare(v, clause.Value) is < 0),
            _ => false
        };
    }

    private static bool EvaluateTier(ConditionClause clause, string? tierName)
    {
        if (clause.Op == ConditionOps.Exists)
            return WantsExists(clause.Value) == (tierName is not null);

        if (tierName is null) return false;

        return clause.Op switch
        {
            ConditionOps.Eq => TierMatches(tierName, clause.Value),
            ConditionOps.Ne => !TierMatches(tierName, clause.Value),
            ConditionOps.In => clause.Value.EnumerateArray().Any(item => TierMatches(tierName, item)),
            _ => false
        };
    }

    private static bool TierMatches(string tierName, JsonElement expected) =>
        expected.ValueKind == JsonValueKind.String &&
        string.Equals(tierName, expected.GetString(), StringComparison.OrdinalIgnoreCase);

    // Dot-path over the payload. Array segments fan out with "any" semantics
    // (items.category matches when ANY item has the category); JSON null counts as absent.
    private static List<JsonElement> ResolvePath(JsonElement root, string path)
    {
        var current = new List<JsonElement> { root };

        foreach (var segment in path.Split('.'))
        {
            var next = new List<JsonElement>();
            foreach (var el in current)
            {
                if (el.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in el.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.Object &&
                            item.TryGetProperty(segment, out var arrChild) &&
                            arrChild.ValueKind != JsonValueKind.Null)
                            next.Add(arrChild);
                }
                else if (el.ValueKind == JsonValueKind.Object &&
                         el.TryGetProperty(segment, out var child) &&
                         child.ValueKind != JsonValueKind.Null)
                {
                    next.Add(child);
                }
            }

            current = next;
            if (current.Count == 0) break;
        }

        // A trailing array value (e.g. tags: ["a","b"]) also fans out to its elements
        var result = new List<JsonElement>();
        foreach (var el in current)
        {
            if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    if (item.ValueKind != JsonValueKind.Null)
                        result.Add(item);
            }
            else
            {
                result.Add(el);
            }
        }

        return result;
    }

    // exists with no value (Undefined) or true → wants the field present; false → wants it absent
    private static bool WantsExists(JsonElement value) =>
        value.ValueKind != JsonValueKind.False;

    private static bool InArray(JsonElement actual, JsonElement expectedArray)
    {
        foreach (var item in expectedArray.EnumerateArray())
            if (ValueEquals(actual, item)) return true;
        return false;
    }

    // Numeric coercion first ("007" == 7, "1.0" == 1), then bool-vs-bool,
    // then case-insensitive string. Cross-kind otherwise never equal.
    private static bool ValueEquals(JsonElement actual, JsonElement expected)
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

    private static int? Compare(JsonElement actual, JsonElement expected)
    {
        if (TryGetDecimal(actual, out var da) && TryGetDecimal(expected, out var de))
            return da.CompareTo(de);
        return null;
    }

    private static bool TryGetDecimal(JsonElement el, out decimal value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number)
            return el.TryGetDecimal(out value);
        if (el.ValueKind == JsonValueKind.String)
            return decimal.TryParse(el.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        return false;
    }
}
