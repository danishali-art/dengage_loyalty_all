using System.Text.Json.Serialization;

namespace dEngage.Loyalty.RuleEngine.Models;

// CR-08 (docs/scope-change-rules A8): per-rule configuration. Named RuleSettings, not
// RuleConfiguration, to avoid confusion with Schema.Configurations.RuleConfiguration (the EF
// IEntityTypeConfiguration<Rule> class) — same problem space, unrelated types.
public class RuleSettings
{
    // null = "inherit from program" (A8's default). Program-level rounding defaults are not
    // wired yet — treated as "down" until Program gets its own default-config column (a
    // Programs-module change outside this CR's file set) — see RulesAppService remarks at the
    // resolution site.
    [JsonPropertyName("rounding")]
    public string? Rounding { get; set; }

    // "Immediate" | "Pending" | "Delayed".
    [JsonPropertyName("posting")]
    public string Posting { get; set; } = "Immediate";

    [JsonPropertyName("holdDays")]
    public int? HoldDays { get; set; }

    [JsonPropertyName("expiryOverrideDays")]
    public int? ExpiryOverrideDays { get; set; }

    [JsonPropertyName("reversible")]
    public bool Reversible { get; set; } = true;

    // Evaluate and audit, post nothing.
    [JsonPropertyName("testMode")]
    public bool TestMode { get; set; }

    [JsonPropertyName("notifyOnAward")]
    public bool NotifyOnAward { get; set; }
}
