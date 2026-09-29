using System.Text.Json;
using System.Text.Json.Serialization;

namespace dEngage.Loyalty.RuleEngine.Models;

// CR-05 (docs/scope-change-rules A5): the grouped AND/OR condition DSL for Rules and Card
// Buckets (they share the `rules` table and its Conditions column). Deliberately a SEPARATE
// type from the flat `ConditionClause`/`ConditionDsl`/`ConditionEvaluator.Evaluate(List<...>)`
// above — Streak Campaigns store their own conditions in a different table
// (`streak_campaigns`) and keep using the flat DSL unchanged; A5's grouped-conditions
// requirement is scoped to Rules, not Streak, and rewriting Streak's condition matching was
// not part of this change.
public sealed class ConditionValue
{
    // "money" | "number" | "string" | "string[]" | "none" (none = exists/is_null, no value needed)
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    // True when the field path is not declared in the trigger event's schema (A5: "flagged in
    // the UI, carry inferred:true in the serialised value") — purely descriptive, does not
    // affect evaluation.
    [JsonPropertyName("inferred")]
    public bool? Inferred { get; set; }
}

public sealed class ConditionLeaf
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = default!;

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = default!;

    [JsonPropertyName("value")]
    public ConditionValue Value { get; set; } = default!;
}

public sealed class ConditionGroup
{
    [JsonPropertyName("op")]
    public string Op { get; set; } = GroupedConditionOps.And;

    [JsonPropertyName("conditions")]
    public List<ConditionLeaf> Conditions { get; set; } = new();
}

public sealed class ConditionTree
{
    [JsonPropertyName("op")]
    public string Op { get; set; } = GroupedConditionOps.And;

    [JsonPropertyName("groups")]
    public List<ConditionGroup> Groups { get; set; } = new();
}

public static class GroupedConditionOps
{
    public const string And = "AND";
    public const string Or = "OR";

    // money, number
    public const string Gte = "gte";
    public const string Lte = "lte";
    public const string Between = "between";
    public const string Eq = "eq";
    public const string Neq = "neq";

    // string
    public const string In = "in";
    public const string NotIn = "not_in";
    public const string StartsWith = "starts_with";

    // undeclared fields only (A5): full set plus these two
    public const string Exists = "exists";
    public const string IsNull = "is_null";

    public static readonly string[] MoneyNumberOps = { Gte, Lte, Between, Eq, Neq };
    public static readonly string[] StringOps = { Eq, Neq, In, NotIn, StartsWith };
    public static readonly string[] UndeclaredOps =
        { Gte, Lte, Between, Eq, Neq, In, NotIn, StartsWith, Exists, IsNull };

    public static readonly string[] All = UndeclaredOps;
}
