using System.Text.Json.Serialization;

namespace dEngage.Loyalty.RuleEngine.Models;

public class RuleLimits
{
    [JsonPropertyName("per_customer_total")]
    public decimal? PerCustomerTotal { get; set; }

    [JsonPropertyName("per_customer_per_day")]
    public decimal? PerCustomerPerDay { get; set; }

    // CR-07 additions (docs/scope-change-rules A7). PerCustomerTotal/PerCustomerPerDay above
    // are kept exactly as they were (same field names, same always-daily/always-Redis-backed
    // behavior) for backward compatibility with already-configured rules — PerCustomerPerPeriod
    // is the new, period-configurable generalization A7 describes, additive rather than a
    // breaking rename.

    [JsonPropertyName("max_per_event")]
    public decimal? MaxPerEvent { get; set; }

    [JsonPropertyName("min_event_amount")]
    public decimal? MinEventAmount { get; set; }

    [JsonPropertyName("cooldown_hours")]
    public decimal? CooldownHours { get; set; }

    [JsonPropertyName("max_customers")]
    public int? MaxCustomers { get; set; }

    [JsonPropertyName("rule_budget_total")]
    public decimal? RuleBudgetTotal { get; set; }

    [JsonPropertyName("rule_budget_per_period")]
    public decimal? RuleBudgetPerPeriod { get; set; }

    [JsonPropertyName("per_customer_per_period")]
    public decimal? PerCustomerPerPeriod { get; set; }

    // "Day" | "Week" | "Month" | "Year" — governs RuleBudgetPerPeriod and PerCustomerPerPeriod.
    [JsonPropertyName("period")]
    public string? Period { get; set; }

    // "Calendar" | "Rolling".
    [JsonPropertyName("reset_window")]
    public string? ResetWindow { get; set; }

    // "Clamp" | "Skip" — applies to RuleBudgetTotal/RuleBudgetPerPeriod/PerCustomerPerPeriod.
    // CooldownHours/MaxCustomers/MinEventAmount are boolean gates with no partial-clamp
    // concept and always skip when breached regardless of this setting.
    [JsonPropertyName("on_breach")]
    public string OnBreach { get; set; } = "Clamp";
}
