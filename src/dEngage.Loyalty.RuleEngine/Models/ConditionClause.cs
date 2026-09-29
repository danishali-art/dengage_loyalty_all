using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace dEngage.Loyalty.RuleEngine.Models;

// DSL v1: [{field, op, value}], dot-path fields (snake_case, case-sensitive),
// clauses combined with AND only (OR = a second rule).
public class ConditionClause
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = default!;

    [JsonPropertyName("op")]
    public string Op { get; set; } = default!;

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }
}

public static class ConditionOps
{
    public const string Eq = "eq";
    public const string Ne = "ne";
    public const string In = "in";
    public const string Gte = "gte";
    public const string Lte = "lte";
    public const string Gt = "gt";
    public const string Lt = "lt";
    public const string Exists = "exists";
    public const string OccurredWithin = "occurred_within";

    public static readonly string[] All = { Eq, Ne, In, Gte, Lte, Gt, Lt, Exists, OccurredWithin };
}

// value shape for occurred_within: {"after_event": "signup", "hours": 24}
public sealed record OccurredWithinValue(string AfterEvent, double Hours)
{
    public static bool TryParse(JsonElement value, out OccurredWithinValue result)
    {
        result = default!;
        if (value.ValueKind != JsonValueKind.Object) return false;
        if (!value.TryGetProperty("after_event", out var ae) ||
            ae.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(ae.GetString()))
            return false;
        if (!value.TryGetProperty("hours", out var h)) return false;

        double hours;
        if (h.ValueKind == JsonValueKind.Number) hours = h.GetDouble();
        else if (h.ValueKind == JsonValueKind.String &&
                 double.TryParse(h.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            hours = parsed;
        else return false;

        result = new OccurredWithinValue(ae.GetString()!, hours);
        return true;
    }
}

public static class ConditionDsl
{
    // event_log retention is 12 months — longer windows would query deleted data
    public const double MaxWindowHours = 8760;

    public static void Validate(List<ConditionClause>? conditions)
    {
        if (conditions is null) return;

        foreach (var clause in conditions)
        {
            if (string.IsNullOrWhiteSpace(clause.Field))
                throw new InvalidOperationException("invalid_condition: field missing");
            if (!ConditionOps.All.Contains(clause.Op))
                throw new InvalidOperationException($"invalid_condition: unknown op '{clause.Op}' (field '{clause.Field}')");

            switch (clause.Op)
            {
                case ConditionOps.In:
                    if (clause.Value.ValueKind != JsonValueKind.Array || clause.Value.GetArrayLength() == 0)
                        throw new InvalidOperationException($"invalid_condition: 'in' requires a non-empty array (field '{clause.Field}')");
                    break;

                case ConditionOps.Gte:
                case ConditionOps.Lte:
                case ConditionOps.Gt:
                case ConditionOps.Lt:
                    if (!IsNumeric(clause.Value))
                        throw new InvalidOperationException($"invalid_condition: '{clause.Op}' requires a numeric value (field '{clause.Field}')");
                    break;

                case ConditionOps.Eq:
                case ConditionOps.Ne:
                    if (clause.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number
                        or JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidOperationException($"invalid_condition: '{clause.Op}' requires a scalar value (field '{clause.Field}')");
                    break;

                case ConditionOps.Exists:
                    if (clause.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Undefined))
                        throw new InvalidOperationException($"invalid_condition: 'exists' requires true/false (field '{clause.Field}')");
                    break;

                case ConditionOps.OccurredWithin:
                    if (!OccurredWithinValue.TryParse(clause.Value, out var w))
                        throw new InvalidOperationException("invalid_condition: 'occurred_within' requires {after_event, hours}");
                    if (w.Hours <= 0 || w.Hours > MaxWindowHours)
                        throw new InvalidOperationException($"invalid_condition: occurred_within.hours must be in (0, {MaxWindowHours}]");
                    break;
            }
        }
    }

    static bool IsNumeric(JsonElement el) =>
        (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out _)) ||
        (el.ValueKind == JsonValueKind.String &&
         decimal.TryParse(el.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out _));
}
