using System.Text.Json;
using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine;

// CR-05 (docs/scope-change-rules A5/A9): structural validation for the grouped condition tree.
// Mirrors ConditionDsl.Validate's scope (structural only — kind-correctness against the trigger
// event's schema is a UI/authoring concern, not enforced here) but adds the two DSL-shape rules
// A9 calls out as blocking: an unsatisfiable AND-group bound, and an empty field path or value.
public static class GroupedConditionDsl
{
    public static void Validate(ConditionTree? tree)
    {
        if (tree is null) return;

        if (tree.Op is not (GroupedConditionOps.And or GroupedConditionOps.Or))
            throw new InvalidOperationException($"invalid_conditions: root op must be AND or OR, got '{tree.Op}'");

        foreach (var group in tree.Groups)
        {
            if (group.Op is not (GroupedConditionOps.And or GroupedConditionOps.Or))
                throw new InvalidOperationException($"invalid_conditions: group op must be AND or OR, got '{group.Op}'");

            var bounds = new Dictionary<string, (decimal? Lo, decimal? Hi)>();

            foreach (var leaf in group.Conditions)
            {
                if (string.IsNullOrWhiteSpace(leaf.Field))
                    throw new InvalidOperationException("invalid_conditions: condition has no field path");
                if (!GroupedConditionOps.All.Contains(leaf.Operator))
                    throw new InvalidOperationException($"invalid_conditions: unknown operator '{leaf.Operator}' (field '{leaf.Field}')");

                ValidateValue(leaf);

                // A9 blocking rule: "field gte X AND field lte Y where X > Y" — only meaningful
                // within an AND-joined group; an OR group can legitimately have both.
                if (group.Op == GroupedConditionOps.And)
                {
                    var (lo, hi) = bounds.GetValueOrDefault(leaf.Field);
                    if (leaf.Operator == GroupedConditionOps.Gte && JsonValueComparison.TryGetDecimal(leaf.Value.Data, out var g))
                        lo = g;
                    if (leaf.Operator == GroupedConditionOps.Lte && JsonValueComparison.TryGetDecimal(leaf.Value.Data, out var l))
                        hi = l;
                    bounds[leaf.Field] = (lo, hi);
                }
            }

            foreach (var (field, (lo, hi)) in bounds)
            {
                if (lo.HasValue && hi.HasValue && lo.Value > hi.Value)
                    throw new InvalidOperationException(
                        $"invalid_conditions: group can never match — '{field}' must be at least {lo} and at most {hi}");
            }
        }
    }

    private static void ValidateValue(ConditionLeaf leaf)
    {
        if (leaf.Operator is GroupedConditionOps.Exists or GroupedConditionOps.IsNull)
            return; // no value required

        switch (leaf.Operator)
        {
            case GroupedConditionOps.In:
            case GroupedConditionOps.NotIn:
                if (leaf.Value?.Data.ValueKind != JsonValueKind.Array || leaf.Value.Data.GetArrayLength() == 0)
                    throw new InvalidOperationException($"invalid_conditions: '{leaf.Operator}' requires a non-empty array (field '{leaf.Field}')");
                break;

            case GroupedConditionOps.Between:
                if (leaf.Value?.Data.ValueKind != JsonValueKind.Array || leaf.Value.Data.GetArrayLength() != 2)
                    throw new InvalidOperationException($"invalid_conditions: 'between' requires a two-element [min, max] array (field '{leaf.Field}')");
                break;

            case GroupedConditionOps.Gte:
            case GroupedConditionOps.Lte:
                if (leaf.Value is null || !JsonValueComparison.TryGetDecimal(leaf.Value.Data, out _))
                    throw new InvalidOperationException($"invalid_conditions: '{leaf.Operator}' requires a numeric value (field '{leaf.Field}')");
                break;

            case GroupedConditionOps.Eq:
            case GroupedConditionOps.Neq:
            case GroupedConditionOps.StartsWith:
                if (leaf.Value is null || string.IsNullOrEmpty(RawText(leaf.Value.Data)))
                    throw new InvalidOperationException($"invalid_conditions: '{leaf.Operator}' has no value (field '{leaf.Field}')");
                break;
        }
    }

    private static string? RawText(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True or JsonValueKind.False => el.GetRawText(),
        _ => null
    };
}
