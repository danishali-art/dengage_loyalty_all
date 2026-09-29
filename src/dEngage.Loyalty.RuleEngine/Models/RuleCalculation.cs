using System.Text.Json.Serialization;

namespace dEngage.Loyalty.RuleEngine.Models;

public class RuleCalculation
{
    [JsonPropertyName("rate")]
    public decimal? Factor { get; set; }

    // FixedBonusRule's fixed amount, and ManualAdjustmentRule's fallback fixed amount when the
    // event payload carries none (both use A3's "amount" calc field name).
    [JsonPropertyName("amount")]
    public decimal? FixedValue { get; set; }

    // CR-02 additions — see docs/scope-change-rules Part A §A3 for the calc field per rule type.

    // RedemptionRule ("points per currency unit"); TransferRule ("transfer ratio").
    [JsonPropertyName("ratio")]
    public decimal? Ratio { get; set; }

    // RedemptionRule: minimum redeemable amount.
    [JsonPropertyName("minRedeem")]
    public decimal? MinRedeem { get; set; }

    // TransferRule: flat fee in points.
    [JsonPropertyName("fee")]
    public decimal? Fee { get; set; }

    // TransferRule: max points transferred per day.
    [JsonPropertyName("maxPerDay")]
    public decimal? MaxPerDay { get; set; }

    // ReversalRule: "proportional" | "full".
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    // ReversalRule: "allow negative" | "clamp to zero" — if the balance is short.
    [JsonPropertyName("allowNegative")]
    public string? AllowNegative { get; set; }

    // ExpiryRule: lot age threshold in days.
    [JsonPropertyName("ageDays")]
    public decimal? AgeDays { get; set; }

    // ExpiryRule consumption order ("FIFO" | "LIFO").
    [JsonPropertyName("order")]
    public string? Order { get; set; }

    // ManualAdjustmentRule reason code: "goodwill" | "correction" | "dispute" | "migration".
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
