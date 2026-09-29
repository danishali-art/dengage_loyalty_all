namespace dEngage.Loyalty.Shared;

// What a reward IS — orthogonal to RewardAcquisition (HOW it's earned). Kept as string
// constants, not a closed enum: new values are meant to be added by extending the
// registry that validates each type's TypeConfig shape (dEngage.Loyalty.Api.Rewards.RewardTypeRegistry),
// not by migrating RewardDefinition's schema.
public static class RewardType
{
    public const string PointsBonus = "points_bonus";
    public const string Discount = "discount";
    public const string Cashback = "cashback";
    public const string FreeProduct = "free_product";
    public const string GiftCard = "gift_card";
    public const string TierUpgrade = "tier_upgrade";
}
