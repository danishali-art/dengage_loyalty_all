namespace dEngage.Loyalty.Shared.Events;

public static class OutboundEventTypes
{
    public const string RewardEarned = "loyalty.reward.earned";
    public const string RewardPurchaseFailed = "loyalty.reward.purchase_failed";
    public const string PointsEarned = "loyalty.points.earned";
    public const string PointsReversed = "loyalty.points.reversed";
    public const string PointsExpired = "loyalty.points.expired";
    public const string PointsTransferred = "loyalty.points.transferred";
    public const string PointsTransferFailed = "loyalty.points.transfer_failed";
    // CR 2026-09-30 §3.9 step 4: redeem reports its outcome the way transfer does.
    public const string PointsRedeemed = "loyalty.points.redeemed";
    public const string PointsRedeemFailed = "loyalty.points.redeem_failed";
    // CR 2026-10-05 item 5 (P-3): a cash.added / cash.spent refused because the wallet's program
    // isn't live (Active + Published). Nothing was posted.
    public const string CashAddFailed = "loyalty.cash.add_failed";
    public const string CashSpendFailed = "loyalty.cash.spend_failed";
    public const string TierChanged = "loyalty.tier.changed";
    public const string StreakCompleted = "loyalty.streak.completed";
    public const string StreakBroken = "loyalty.streak.broken";

    // CR-08 (A8): emitted once per applied rule whose Configuration.notifyOnAward is true, in
    // addition to (not instead of) PointsEarned's per-event account summary.
    public const string RuleAwarded = "loyalty.rule.awarded";
}
