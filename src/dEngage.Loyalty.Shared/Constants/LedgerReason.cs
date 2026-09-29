namespace dEngage.Loyalty.Shared;

public static class LedgerReason
{
    public const string Earn = "earn";
    public const string StampEarn = "stamp_earn";
    public const string StampReset = "stamp_reset";
    public const string CashLoad = "cash_load";
    public const string CashSpend = "cash_spend";
    public const string PointsRedeemed = "points_redeemed";
    public const string PointsRedeemedCash = "points_redeemed_cash";
    public const string Refund = "refund";
    public const string PointsExpired = "points_expired";
    public const string RewardPurchase = "reward_purchase";
    public const string TransferOut = "transfer_out";
    public const string TransferIn = "transfer_in";

    // CR-02: ManualAdjustmentRule postings (points.adjusted). Distinct from the legacy
    // PointsRedeemed/PointsExpired reasons above so an operator correction is never confused
    // with a customer-initiated redemption or a scheduled expiry in the ledger/audit trail.
    public const string PointsAdjusted = "points_adjusted";

    // CR-02: ReversalRule postings. Deliberately distinct from the legacy Refund reason —
    // ReversalRuleProcessor is not wired into the live order.refunded path (see
    // docs/scope-changes changelog: left disconnected pending an explicit coexistence decision
    // with RefundService) and must never share its cumulative-refund-cap bookkeeping with it.
    public const string RuleReversal = "rule_reversal";
}
