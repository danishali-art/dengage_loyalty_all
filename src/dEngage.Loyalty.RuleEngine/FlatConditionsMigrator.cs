using System.Text.Json;
using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine;

// CR-05 (docs/scope-change-rules, Part B migration notes #2): converts an existing rule's flat
// ConditionClause list into the grouped ConditionTree shape, wrapping it in a single AND group
// under an AND root — this is the one-time backfill every existing `rules` row (Rules and
// Card-Bucket-tagged alike) needs run against it before the CR-11 UI deploys. Pure/testable by
// design; the operational step of running it against every tenant's data is a deployment task
// (see docs/scope-changes changelog), not something exercised against a live database here.
public static class FlatConditionsMigrator
{
    // Every reader of `rules.conditions` (RuleCacheService's matching path, RulesAppService and
    // CardBucketsAppService's admin API responses) hits the same problem: a row written before
    // this branch's grouped-condition migration still holds the old flat `[{field,op,value}]`
    // shape (a JSON array at the root) rather than the new `{op,groups}` tree (a JSON object).
    // RuleCacheService self-heals and persists the conversion on its own read path (see its
    // remarks) — this is the parse-only half of that same conversion, shared so every other
    // reader degrades to "read the old shape correctly" instead of throwing, even for a row
    // RuleCacheService hasn't reached yet (disabled/pending rules, or before the first sync).
    public static ConditionTree? ParseConditions(string? json) => ParseConditions(json, out _);

    /// <param name="wasFlat">True when `json` was the old shape and had to be converted —
    /// RuleCacheService uses this to decide whether the row needs writing back.</param>
    public static ConditionTree? ParseConditions(string? json, out bool wasFlat)
    {
        wasFlat = false;
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return JsonSerializer.Deserialize<ConditionTree>(json);

        wasFlat = true;
        return ToGroupedTree(JsonSerializer.Deserialize<List<ConditionClause>>(json));
    }

    public static ConditionTree? ToGroupedTree(List<ConditionClause>? flat)
    {
        if (flat is null || flat.Count == 0) return null;

        var leaves = flat.Select(ToLeaf).Where(l => l is not null).Select(l => l!).ToList();
        if (leaves.Count == 0) return null;

        return new ConditionTree
        {
            Op = GroupedConditionOps.And,
            Groups = new List<ConditionGroup> { new() { Op = GroupedConditionOps.And, Conditions = leaves } }
        };
    }

    private static ConditionLeaf? ToLeaf(ConditionClause c)
    {
        // occurred_within -> agg.hoursSince.<afterEvent> <= hours (decision #3: map onto the
        // new namespace model rather than dropping the capability).
        if (c.Op == ConditionOps.OccurredWithin && OccurredWithinValue.TryParse(c.Value, out var window))
        {
            return new ConditionLeaf
            {
                Field = $"agg.hoursSince.{window.AfterEvent}",
                Operator = GroupedConditionOps.Lte,
                Value = new ConditionValue { Type = "number", Data = JsonSerializer.SerializeToElement(window.Hours) }
            };
        }

        // The old special-cased "tier" field -> agg.tier (server-computed, see
        // GroupedConditionEvaluator remarks on why this is NOT profile.tier).
        var field = c.Field == "tier" ? "agg.tier" : c.Field;

        // A5's operator set has no strict gt/lt — fold into gte/lte (decision #3: documented,
        // minor boundary-inclusive loosening rather than a dropped capability).
        var op = c.Op switch
        {
            ConditionOps.Gt => GroupedConditionOps.Gte,
            ConditionOps.Lt => GroupedConditionOps.Lte,
            ConditionOps.Ne => GroupedConditionOps.Neq,
            ConditionOps.Eq => GroupedConditionOps.Eq,
            ConditionOps.In => GroupedConditionOps.In,
            ConditionOps.Gte => GroupedConditionOps.Gte,
            ConditionOps.Lte => GroupedConditionOps.Lte,
            ConditionOps.Exists => GroupedConditionOps.Exists,
            _ => (string?)null
        };
        if (op is null) return null;

        return new ConditionLeaf { Field = field, Operator = op, Value = ToValue(op, c.Value) };
    }

    private static ConditionValue ToValue(string op, JsonElement raw)
    {
        if (op is GroupedConditionOps.Exists)
            return new ConditionValue { Type = "none", Data = raw };

        var type = raw.ValueKind switch
        {
            JsonValueKind.Array => "string[]",
            JsonValueKind.Number => "number",
            _ => "string"
        };
        return new ConditionValue { Type = type, Data = raw };
    }
}
