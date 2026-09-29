using System.Text.Json;
using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine;

// CR-05 (docs/scope-change-rules A5): evaluates the grouped AND/OR condition tree for Rules and
// Card Buckets. See ConditionTree.cs for why this is a separate DSL from the flat
// ConditionEvaluator that Streak Campaigns still use.
public static class GroupedConditionEvaluator
{
    private const string HourOfDayField = "event.hour_of_day";
    private const string DayOfWeekField = "event.day_of_week";
    private const string TierField = "agg.tier";
    private const string HoursSincePrefix = "agg.hoursSince.";
    private static readonly string[] DayNames =
        { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

    public static bool Evaluate(ConditionTree? tree, EvaluationEvent evt, ConditionContext context)
    {
        if (tree is null || tree.Groups.Count == 0) return true;

        var results = tree.Groups.Select(g => EvaluateGroup(g, evt, context));
        return tree.Op == GroupedConditionOps.Or ? results.Any(r => r) : results.All(r => r);
    }

    private static bool EvaluateGroup(ConditionGroup group, EvaluationEvent evt, ConditionContext context)
    {
        if (group.Conditions.Count == 0) return true;
        var results = group.Conditions.Select(c => EvaluateLeaf(c, evt, context));
        return group.Op == GroupedConditionOps.Or ? results.Any(r => r) : results.All(r => r);
    }

    private static bool EvaluateLeaf(ConditionLeaf leaf, EvaluationEvent evt, ConditionContext context)
    {
        var values = ResolveValues(leaf.Field, evt, context);

        if (leaf.Operator == GroupedConditionOps.Exists)
            return WantsExists(leaf.Value.Data) == values.Count > 0;

        if (values.Count == 0)
        {
            // Missing-field semantics (A5): false for positive operators, true for
            // not_in/neq/is_null — a rule guarding against an unwanted value should not
            // accidentally match just because the caller omitted the field.
            return leaf.Operator is GroupedConditionOps.NotIn or GroupedConditionOps.Neq or GroupedConditionOps.IsNull;
        }

        if (leaf.Operator == GroupedConditionOps.IsNull) return false; // field is present

        return leaf.Operator switch
        {
            GroupedConditionOps.Eq => values.Any(v => JsonValueComparison.ValueEquals(v, leaf.Value.Data)),
            GroupedConditionOps.Neq => !values.Any(v => JsonValueComparison.ValueEquals(v, leaf.Value.Data)),
            GroupedConditionOps.In => values.Any(v => InArray(v, leaf.Value.Data)),
            GroupedConditionOps.NotIn => !values.Any(v => InArray(v, leaf.Value.Data)),
            GroupedConditionOps.Gte => values.Any(v => JsonValueComparison.Compare(v, leaf.Value.Data) is >= 0),
            GroupedConditionOps.Lte => values.Any(v => JsonValueComparison.Compare(v, leaf.Value.Data) is <= 0),
            GroupedConditionOps.Between => values.Any(v => InBetween(v, leaf.Value.Data)),
            GroupedConditionOps.StartsWith => values.Any(v => JsonValueComparison.StartsWith(v, leaf.Value.Data)),
            _ => false
        };
    }

    // agg.tier / agg.hoursSince.<eventType> resolve from ConditionContext (server-computed
    // state), never from the payload — see ConditionTree.cs remarks on why old "tier" maps to
    // agg.tier rather than profile.tier: profile.* is a caller-supplied payload snapshot, and
    // mapping a server-authoritative tier check onto caller-supplied data would silently change
    // what the rule actually verifies.
    private static List<JsonElement> ResolveValues(string field, EvaluationEvent evt, ConditionContext context)
    {
        if (field == TierField)
            return context.CustomerTierName is null
                ? new List<JsonElement>()
                : new List<JsonElement> { JsonSerializer.SerializeToElement(context.CustomerTierName) };

        if (field.StartsWith(HoursSincePrefix, StringComparison.Ordinal))
        {
            var eventType = field[HoursSincePrefix.Length..];
            if (!context.LatestEventAt.TryGetValue(eventType, out var lastAt))
                return new List<JsonElement>();
            var hours = (evt.OccurredAt - lastAt).TotalHours;
            return hours < 0 ? new List<JsonElement>() : new List<JsonElement> { JsonSerializer.SerializeToElement(hours) };
        }

        if (field == HourOfDayField)
            return new List<JsonElement> { JsonSerializer.SerializeToElement(evt.OccurredAt.Hour) };
        if (field == DayOfWeekField)
            return new List<JsonElement> { JsonSerializer.SerializeToElement(DayNames[(int)evt.OccurredAt.DayOfWeek]) };

        return ResolvePath(evt.Data, field);
    }

    // Dot-path over the payload. Array segments fan out with "any" semantics; JSON null counts
    // as absent. Identical algorithm to the flat ConditionEvaluator's ResolvePath, kept as a
    // separate copy rather than shared — see ConditionTree.cs remarks on why the two DSLs stay
    // independent.
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

    private static bool WantsExists(JsonElement value) =>
        value.ValueKind != JsonValueKind.False;

    private static bool InArray(JsonElement actual, JsonElement expectedArray)
    {
        if (expectedArray.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in expectedArray.EnumerateArray())
            if (JsonValueComparison.ValueEquals(actual, item)) return true;
        return false;
    }

    private static bool InBetween(JsonElement actual, JsonElement bounds)
    {
        if (bounds.ValueKind != JsonValueKind.Array || bounds.GetArrayLength() != 2) return false;
        var arr = bounds.EnumerateArray().ToList();
        var lo = JsonValueComparison.Compare(actual, arr[0]);
        var hi = JsonValueComparison.Compare(actual, arr[1]);
        return lo is >= 0 && hi is <= 0;
    }
}
