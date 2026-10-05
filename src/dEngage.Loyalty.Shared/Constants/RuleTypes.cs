namespace dEngage.Loyalty.Shared;

// Streak used to be a fourth RuleType here — it's now its own campaign type entirely
// (dEngage.Loyalty.RuleEngine.Campaigns.CampaignTypes.Streak, backed by its own streak_campaigns
// table), not a case of a generic Rule.
public static class RuleTypes
{
    public const string SpendRule = "SpendRule";
    public const string FixedBonusRule = "FixedBonusRule";

    // CR-02 (docs/scope-change-rules): Burn/Reverse/Adjust rule types. Additive — the existing
    // points.redeem/points.transfer/order.refunded handlers keep their own AccountType.Config-
    // driven logic unchanged; these become available for newly-configured rules going forward
    // (see docs/scope-changes changelog for this branch).
    public const string RedemptionRule = "RedemptionRule";
    public const string TransferRule = "TransferRule";
    public const string ReversalRule = "ReversalRule";
    public const string ManualAdjustmentRule = "ManualAdjustmentRule";

    // CR 2026-10-05 (D1, D15): retired — no new rule can use them and existing rows were disabled
    // by migration. Kept because rules/rule_versions rows still carry them as history.
    // ExpiryRule never fired (nothing publishes points.expired); wallet expiry is
    // PointsExpirationJob, which doesn't use rules.
    public const string StampRule = "StampRule";
    public const string ExpiryRule = "ExpiryRule";

    public static readonly string[] All =
    {
        SpendRule, FixedBonusRule,
        RedemptionRule, TransferRule, ReversalRule, ManualAdjustmentRule
    };

    public static bool IsRetired(string ruleType) => ruleType is StampRule or ExpiryRule;

    // TransferRule/ReversalRule bypass the shared WinnerSelector/LedgerPoster pipeline
    // (dual-entry posting / inherited target account respectively — see
    // RuleEngine.Processing.TransferRuleProcessor / ReversalRuleProcessor).
    public static bool UsesWinnerSelectorPipeline(string ruleType) =>
        ruleType != TransferRule && ruleType != ReversalRule;
}
