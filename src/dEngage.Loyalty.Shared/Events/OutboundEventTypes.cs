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
    public const string TierChanged = "loyalty.tier.changed";
    public const string StreakCompleted = "loyalty.streak.completed";
    public const string StreakBroken = "loyalty.streak.broken";

    // CR-08 (A8): emitted once per applied rule whose Configuration.notifyOnAward is true, in
    // addition to (not instead of) PointsEarned's per-event account summary.
    public const string RuleAwarded = "loyalty.rule.awarded";
}
