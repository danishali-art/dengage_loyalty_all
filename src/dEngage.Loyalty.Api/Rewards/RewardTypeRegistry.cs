using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Api.Rewards;

// The one seam RewardType-specific shape validation goes through. Adding a reward type means
// adding an entry here — not a migration, not a new required column on RewardDefinition, and
// no branching in RewardsAppService or any downstream consumer.
//
// CR 2026-09-30 (A1): only the creatable types are registered. The retired ones
// (RewardType.Retired) stay readable as stored rows, but their TypeConfig can no longer be
// created or edited, so they have no validator here.
public static class RewardTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, Func<JsonElement, List<string>>> Validators =
        new Dictionary<string, Func<JsonElement, List<string>>>
        {
            [RewardType.Cashback] = ValidateCashback,
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

    // CR 2026-09-30 (§3.3): the engine credits `amount` to `cash_account_type_id`, so the
    // currency comes from the CASH whitelist and the wallet is required. That the wallet exists,
    // is CASH, belongs to this tenant + program and holds `currency` needs the DB — checked in
    // RewardsAppService, not here.
    private static List<string> ValidateCashback(JsonElement cfg)
    {
        var errors = new List<string>();
        RequireDecimalString(cfg, "amount", errors, mustBePositive: true);
        if (!TryGetProperty(cfg, "currency", out var currency) || currency.ValueKind != JsonValueKind.String
            || !SupportedCurrencies.IsSupported(currency.GetString()))
            errors.Add($"type_config.currency must be one of: {string.Join(", ", SupportedCurrencies.All)}.");
        RequireGuid(cfg, "cash_account_type_id", errors);
        return errors;
    }

    // Tier ownership (same tenant + program) is checked in RewardsAppService.
    private static List<string> ValidateTierUpgrade(JsonElement cfg)
    {
        var errors = new List<string>();
        RequireGuid(cfg, "target_tier_id", errors);
        // Optional: absent/null = the normal tier lifecycle applies (O4); otherwise a whole
        // number of days the upgrade is protected from the nightly downgrade.
        if (TryGetProperty(cfg, "duration_days", out var days) && days.ValueKind != JsonValueKind.Null
            && (days.ValueKind != JsonValueKind.Number || !days.TryGetInt32(out var d) || d <= 0))
            errors.Add("type_config.duration_days must be a whole number greater than 0, or omitted.");
        return errors;
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

    private static bool TryGetProperty(JsonElement cfg, string field, out JsonElement value)
    {
        value = default;
        return cfg.ValueKind == JsonValueKind.Object && cfg.TryGetProperty(field, out value);
    }
}
