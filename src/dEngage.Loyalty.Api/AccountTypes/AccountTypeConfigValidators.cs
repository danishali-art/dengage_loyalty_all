using System.Text.Json;
using dEngage.Loyalty.Api.Framework.ErrorHandling;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Api.AccountTypes;

// Strategy pattern (plan §2): one validator per AccountType.Type, selected at runtime by
// AccountTypeConfigValidatorSelector rather than a branching if/switch scattered through the app
// service.
public interface IAccountTypeConfigValidator
{
    string Type { get; }
    void Validate(JsonElement config);
}

public sealed class PointsAccountTypeConfigValidator : IAccountTypeConfigValidator
{
    public string Type => "POINTS";

    public void Validate(JsonElement config)
    {
        if (!config.TryGetProperty("decimals", out var decimals) || decimals.ValueKind != JsonValueKind.Number)
            throw new ValidationApiException("POINTS config requires a numeric 'decimals' field.");
        // 1.3.CL item 2: decimals now sets the precision Spend rules round down to, and the
        // ledger stores numeric(20,4) — anything beyond 4 places could not be posted.
        if (!decimals.TryGetInt32(out var places) || places is < 0 or > 4)
            throw new ValidationApiException("POINTS 'decimals' must be a whole number from 0 to 4.");

        int? expirationDays = null;
        if (config.TryGetProperty("expiration_days", out var expiration) && expiration.ValueKind != JsonValueKind.Null)
        {
            if (!expiration.TryGetInt32(out var days) || days <= 0)
                throw new ValidationApiException("POINTS 'expiration_days' must be a whole number greater than 0.");
            expirationDays = days;
        }

        // 1.3.CL item 1: the "expiring soon" warning moved here from Program. Same bound
        // PointsExpiringDetectorJob enforces — a warning is only meaningful before expiry.
        if (config.TryGetProperty("warning_days", out var warning) && warning.ValueKind != JsonValueKind.Null)
        {
            if (!warning.TryGetInt32(out var warningDays) || warningDays <= 0)
                throw new ValidationApiException("POINTS 'warning_days' must be a whole number greater than 0.");
            if (expirationDays is null)
                throw new ValidationApiException("POINTS 'warning_days' requires 'expiration_days'.");
            if (warningDays >= expirationDays)
                throw new ValidationApiException("POINTS 'warning_days' must be less than 'expiration_days'.");
        }

        if (config.TryGetProperty("redemption", out var redemption) && redemption.ValueKind == JsonValueKind.Object)
        {
            if (!redemption.TryGetProperty("rate", out _) || !redemption.TryGetProperty("min_points", out _))
                throw new ValidationApiException("POINTS 'redemption' config requires 'rate' and 'min_points'.");

            // PointsRedeemHandler.RedemptionConfig.TargetAccountTypeId is a non-nullable Guid — an
            // absent field deserializes to Guid.Empty rather than failing, which only surfaces later
            // as an FK violation on customer_accounts at redeem time. Reject it here instead.
            if (!redemption.TryGetProperty("target_account_type_id", out var targetId) ||
                targetId.ValueKind != JsonValueKind.String || !Guid.TryParse(targetId.GetString(), out _))
                throw new ValidationApiException("POINTS 'redemption' config requires a 'target_account_type_id' (the wallet redeemed points convert into).");
        }
    }
}

public sealed class CashAccountTypeConfigValidator : IAccountTypeConfigValidator
{
    public string Type => "CASH";

    public void Validate(JsonElement config)
    {
        if (!config.TryGetProperty("currency", out var currency) || currency.ValueKind != JsonValueKind.String)
            throw new ValidationApiException("CASH config requires a string 'currency' field.");
        // 1.3.CL item 4: a fixed whitelist, not free text.
        if (!SupportedCurrencies.IsSupported(currency.GetString()))
            throw new ValidationApiException(
                $"CASH 'currency' must be one of: {string.Join(", ", SupportedCurrencies.All)}.");
        if (!config.TryGetProperty("decimals", out var decimals) || decimals.ValueKind != JsonValueKind.Number)
            throw new ValidationApiException("CASH config requires a numeric 'decimals' field.");
        // 1.3.CL item 3: CASH is real credit and cannot expire inside the loyalty system.
        if (config.TryGetProperty("expiration_days", out _))
            throw new ValidationApiException("CASH config cannot have 'expiration_days' — cash balances do not expire.");
    }
}

// No admin UI form exists yet for STAMP (per ux/en/fintech) — accept any JSON object rather than
// guessing a shape product hasn't defined.
public sealed class StampAccountTypeConfigValidator : IAccountTypeConfigValidator
{
    public string Type => "STAMP";

    public void Validate(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
            throw new ValidationApiException("STAMP config must be a JSON object.");
    }
}

public sealed class AccountTypeConfigValidatorSelector(IEnumerable<IAccountTypeConfigValidator> validators)
{
    public void Validate(string type, string configJson)
    {
        var validator = validators.FirstOrDefault(v => v.Type == type)
            ?? throw new ValidationApiException($"Unknown account type '{type}'. Must be POINTS, CASH, or STAMP.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(configJson);
        }
        catch (JsonException)
        {
            throw new ValidationApiException("config must be valid JSON.");
        }

        validator.Validate(document.RootElement);
    }
}
