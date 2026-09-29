using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Api.Rewards;

// The one seam RewardType-specific shape validation goes through. Adding a reward type means
// adding an entry here — not a migration, not a new required column on RewardDefinition, and
// no branching in RewardsAppService or any downstream consumer.
public static class RewardTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, Func<JsonElement, List<string>>> Validators =
        new Dictionary<string, Func<JsonElement, List<string>>>
        {
            [RewardType.PointsBonus] = ValidatePointsBonus,
            [RewardType.Discount] = ValidateDiscount,
            [RewardType.Cashback] = ValidateCashback,
            [RewardType.FreeProduct] = ValidateFreeProduct,
            [RewardType.GiftCard] = ValidateGiftCard,
            [RewardType.TierUpgrade] = ValidateTierUpgrade,
        };

    public static IReadOnlyCollection<string> KnownTypes => Validators.Keys.ToArray();

    public static bool IsKnown(string rewardType) => Validators.ContainsKey(rewardType);

    public static List<string> ValidateTypeConfig(string rewardType, JsonElement typeConfig)
    {
        if (!Validators.TryGetValue(rewardType, out var validate))
            return [$"Unknown RewardType '{rewardType}'."];
        return validate(typeConfig);
    }

    private static List<string> ValidatePointsBonus(JsonElement cfg)
    {
        var errors = new List<string>();
        RequireDecimalString(cfg, "amount", errors, mustBePositive: true);
        RequireGuid(cfg, "account_type_id", errors);
        return errors;
    }

    private static List<string> ValidateDiscount(JsonElement cfg)
    {
        var errors = new List<string>();
        RequireOneOf(cfg, "discount_kind", ["percentage", "fixed"], errors);
        RequireDecimalString(cfg, "value", errors, mustBePositive: true);
        return errors;
    }

    private static List<string> ValidateCashback(JsonElement cfg)
    {
        var errors = new List<string>();
        RequireDecimalString(cfg, "amount", errors, mustBePositive: true);
        RequireString(cfg, "currency", errors);
        return errors;
    }

    private static List<string> ValidateFreeProduct(JsonElement cfg)
    {
        var errors = new List<string>();
        RequireString(cfg, "product_sku", errors);
        RequireNumber(cfg, "quantity", errors, mustBePositive: true);
        return errors;
    }

    private static List<string> ValidateGiftCard(JsonElement cfg)
    {
        var errors = new List<string>();
        RequireDecimalString(cfg, "value", errors, mustBePositive: true);
        RequireString(cfg, "currency", errors);
        return errors;
    }

    private static List<string> ValidateTierUpgrade(JsonElement cfg)
    {
        var errors = new List<string>();
        RequireGuid(cfg, "target_tier_id", errors);
        return errors;
    }

    private static void RequireString(JsonElement cfg, string field, List<string> errors)
    {
        if (!TryGetProperty(cfg, field, out var v) || v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
            errors.Add($"type_config.{field} is required.");
    }

    private static void RequireNumber(JsonElement cfg, string field, List<string> errors, bool mustBePositive = false)
    {
        if (!TryGetProperty(cfg, field, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            errors.Add($"type_config.{field} is required and must be a number.");
            return;
        }
        if (mustBePositive && v.GetDecimal() <= 0)
            errors.Add($"type_config.{field} must be greater than 0.");
    }

    // Matches the frontend's DecimalString convention (shared/money/decimal-string.ts) — money
    // travels as a JSON string, never a bare JS-precision number. Up to 16 integer digits + up
    // to 4 fractional, matching numeric(20,4).
    private static readonly Regex DecimalStringPattern = new(@"^-?\d{1,16}(\.\d{1,4})?$", RegexOptions.Compiled);

    private static void RequireDecimalString(JsonElement cfg, string field, List<string> errors, bool mustBePositive = false)
    {
        if (!TryGetProperty(cfg, field, out var v) || v.ValueKind != JsonValueKind.String || !DecimalStringPattern.IsMatch(v.GetString() ?? string.Empty))
        {
            errors.Add($"type_config.{field} is required and must be a decimal string (e.g. \"19.99\").");
            return;
        }
        if (mustBePositive && decimal.Parse(v.GetString()!, CultureInfo.InvariantCulture) <= 0)
            errors.Add($"type_config.{field} must be greater than 0.");
    }

    private static void RequireGuid(JsonElement cfg, string field, List<string> errors)
    {
        if (!TryGetProperty(cfg, field, out var v) || v.ValueKind != JsonValueKind.String || !Guid.TryParse(v.GetString(), out _))
            errors.Add($"type_config.{field} is required and must be a valid id.");
    }

    private static void RequireOneOf(JsonElement cfg, string field, string[] allowed, List<string> errors)
    {
        if (!TryGetProperty(cfg, field, out var v) || v.ValueKind != JsonValueKind.String || !allowed.Contains(v.GetString()))
            errors.Add($"type_config.{field} must be one of: {string.Join(", ", allowed)}.");
    }

    private static bool TryGetProperty(JsonElement cfg, string field, out JsonElement value)
    {
        value = default;
        return cfg.ValueKind == JsonValueKind.Object && cfg.TryGetProperty(field, out value);
    }
}
