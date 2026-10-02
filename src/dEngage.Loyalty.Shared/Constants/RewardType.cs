namespace dEngage.Loyalty.Shared;

// What a reward IS — orthogonal to RewardAcquisition (HOW it's earned). Kept as string
// constants, not a closed enum: new values are meant to be added by extending the
// registry that validates each type's TypeConfig shape (dEngage.Loyalty.Api.Rewards.RewardTypeRegistry),
// not by migrating RewardDefinition's schema.
public static class RewardType
{
    public const string Cashback = "cashback";
    public const string TierUpgrade = "tier_upgrade";

    // CR 2026-09-30 (A1): retired — no new reward can use them, existing rows were deactivated.
    // Kept because they are stored values (reward_definitions, publish snapshots) that the API
    // still reads, lists and returns.
    public const string PointsBonus = "points_bonus";
    public const string Discount = "discount";
    public const string FreeProduct = "free_product";
    public const string GiftCard = "gift_card";

    /// <summary>The reward types a reward can be created with (CR 2026-09-30).</summary>
    public static readonly IReadOnlyList<string> Creatable = [Cashback, TierUpgrade];

    public static readonly IReadOnlyList<string> Retired = [PointsBonus, Discount, FreeProduct, GiftCard];

    public static bool IsCreatable(string rewardType) => Creatable.Contains(rewardType);

    // CR 2026-09-30 compatibility matrix: cashback with either acquisition, tier_upgrade only
    // through a streak (a purchasable tier would sidestep the tier ladder entirely).
    public static bool IsAllowedWith(string rewardType, string acquisition) =>
        rewardType switch
        {
            Cashback => acquisition is RewardAcquisition.PointsPurchase or RewardAcquisition.StreakCompletion,
            TierUpgrade => acquisition == RewardAcquisition.StreakCompletion,
            _ => false
        };
}
