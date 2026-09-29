using System.Text.Json;
using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.Api.CardBuckets;

// Translates the Card Buckets form's structured fields to/from the single-AND-group
// ConditionTree a FixedBonusRule actually stores (CR-05 — see ConditionTree.cs). Encode always
// emits the same canonical leaf shapes inside one AND group under an AND root; decode scans
// that group for those exact shapes and leaves anything else untouched as "additional
// conditions" — so a bucket built here round-trips losslessly, and a rule that also carries
// hand-authored leaves (via the escape hatch) keeps them on edit.
public static class CardBucketConditionMapper
{
    private const string TxStatusCaptured = "captured";

    public static ConditionTree? ToConditions(
        List<string>? mccCodes, decimal? amountMin, decimal? amountMax,
        string? countryMode, List<string>? countries, bool requireCaptured,
        int? hourFrom, int? hourTo, List<string>? daysOfWeek,
        List<ConditionLeaf>? additionalConditions)
    {
        var leaves = new List<ConditionLeaf>();

        if (mccCodes is { Count: > 0 })
            leaves.Add(Leaf("mcc", GroupedConditionOps.In, StringArrayValue(mccCodes)));

        if (amountMin is not null) leaves.Add(Leaf("amount", GroupedConditionOps.Gte, MoneyValue(amountMin.Value)));
        if (amountMax is not null) leaves.Add(Leaf("amount", GroupedConditionOps.Lte, MoneyValue(amountMax.Value)));

        if (countries is { Count: > 0 })
        {
            if (countryMode == "not_in")
                leaves.Add(Leaf("country", GroupedConditionOps.NotIn, StringArrayValue(countries)));
            else
                leaves.Add(Leaf("country", GroupedConditionOps.In, StringArrayValue(countries)));
        }

        if (requireCaptured)
            leaves.Add(Leaf("tx_status", GroupedConditionOps.Eq, StringValue(TxStatusCaptured)));

        if (hourFrom is not null) leaves.Add(Leaf("event.hour_of_day", GroupedConditionOps.Gte, NumberValue(hourFrom.Value)));
        if (hourTo is not null) leaves.Add(Leaf("event.hour_of_day", GroupedConditionOps.Lte, NumberValue(hourTo.Value)));

        if (daysOfWeek is { Count: > 0 })
            leaves.Add(Leaf("event.day_of_week", GroupedConditionOps.In, StringArrayValue(daysOfWeek)));

        if (additionalConditions is { Count: > 0 })
            leaves.AddRange(additionalConditions);

        if (leaves.Count == 0) return null;

        return new ConditionTree
        {
            Op = GroupedConditionOps.And,
            Groups = new List<ConditionGroup> { new() { Op = GroupedConditionOps.And, Conditions = leaves } }
        };
    }

    public static RuleLimits? ToLimits(decimal? perCustomerPerDay, decimal? perCustomerTotal) =>
        perCustomerPerDay is null && perCustomerTotal is null
            ? null
            : new RuleLimits { PerCustomerPerDay = perCustomerPerDay, PerCustomerTotal = perCustomerTotal };

    public sealed record DecodedFields(
        List<string>? MccCodes, decimal? AmountMin, decimal? AmountMax,
        string? CountryMode, List<string>? Countries, bool RequireCaptured,
        int? HourFrom, int? HourTo, List<string>? DaysOfWeek,
        List<ConditionLeaf>? AdditionalConditions);

    // Decodes only the first (and, for a bucket built by this mapper, only) group — a rule
    // carrying more than one group or an OR-joined root is not something this mapper produced,
    // so everything beyond group 1 is preserved verbatim as "additional conditions" alongside
    // any unrecognised leaf inside group 1.
    public static DecodedFields FromConditions(ConditionTree? conditions)
    {
        var firstGroup = conditions?.Groups.FirstOrDefault()?.Conditions ?? new List<ConditionLeaf>();
        var remaining = new List<ConditionLeaf>(firstGroup);
        List<string>? mccCodes = null;
        decimal? amountMin = null, amountMax = null;
        string? countryMode = null;
        List<string>? countries = null;
        var requireCaptured = false;
        int? hourFrom = null, hourTo = null;
        List<string>? daysOfWeek = null;

        foreach (var c in firstGroup)
        {
            if (c.Field == "mcc" && c.Operator == GroupedConditionOps.In && mccCodes is null)
            {
                mccCodes = ReadStringArray(c.Value.Data);
                remaining.Remove(c);
            }
            else if (c.Field == "amount" && c.Operator == GroupedConditionOps.Gte && amountMin is null)
            {
                amountMin = ReadDecimal(c.Value.Data);
                remaining.Remove(c);
            }
            else if (c.Field == "amount" && c.Operator == GroupedConditionOps.Lte && amountMax is null)
            {
                amountMax = ReadDecimal(c.Value.Data);
                remaining.Remove(c);
            }
            else if (c.Field == "country" && c.Operator == GroupedConditionOps.In && countries is null)
            {
                countryMode = "in";
                countries = ReadStringArray(c.Value.Data);
                remaining.Remove(c);
            }
            else if (c.Field == "country" && c.Operator == GroupedConditionOps.NotIn && countries is null)
            {
                countryMode = "not_in";
                countries = ReadStringArray(c.Value.Data);
                remaining.Remove(c);
            }
            else if (c.Field == "tx_status" && c.Operator == GroupedConditionOps.Eq &&
                     string.Equals(ReadString(c.Value.Data), TxStatusCaptured, StringComparison.OrdinalIgnoreCase))
            {
                requireCaptured = true;
                remaining.Remove(c);
            }
            else if (c.Field == "event.hour_of_day" && c.Operator == GroupedConditionOps.Gte && hourFrom is null)
            {
                hourFrom = (int?)ReadDecimal(c.Value.Data);
                remaining.Remove(c);
            }
            else if (c.Field == "event.hour_of_day" && c.Operator == GroupedConditionOps.Lte && hourTo is null)
            {
                hourTo = (int?)ReadDecimal(c.Value.Data);
                remaining.Remove(c);
            }
            else if (c.Field == "event.day_of_week" && c.Operator == GroupedConditionOps.In && daysOfWeek is null)
            {
                daysOfWeek = ReadStringArray(c.Value.Data);
                remaining.Remove(c);
            }
        }

        return new DecodedFields(
            mccCodes, amountMin, amountMax, countryMode, countries, requireCaptured,
            hourFrom, hourTo, daysOfWeek, remaining.Count > 0 ? remaining : null);
    }

    private static ConditionLeaf Leaf(string field, string op, ConditionValue value) => new()
    {
        Field = field,
        Operator = op,
        Value = value
    };

    private static ConditionValue MoneyValue(decimal amount) =>
        new() { Type = "money", Data = JsonSerializer.SerializeToElement(amount) };

    private static ConditionValue NumberValue(decimal number) =>
        new() { Type = "number", Data = JsonSerializer.SerializeToElement(number) };

    private static ConditionValue StringValue(string value) =>
        new() { Type = "string", Data = JsonSerializer.SerializeToElement(value) };

    private static ConditionValue StringArrayValue(List<string> values) =>
        new() { Type = "string[]", Data = JsonSerializer.SerializeToElement(values) };

    private static List<string> ReadStringArray(JsonElement el) =>
        el.ValueKind == JsonValueKind.Array ? el.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new();

    private static decimal ReadDecimal(JsonElement el) =>
        el.ValueKind == JsonValueKind.Number ? el.GetDecimal() : decimal.Parse(el.GetString()!);

    private static string? ReadString(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
