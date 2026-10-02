namespace dEngage.Loyalty.Shared;

public static class RewardAcquisition
{
    // CR 2026-09-30 (A1): retired — no new reward can use it, existing rows were deactivated.
    // Kept because it is a stored value (reward_definitions, RewardLog history) and the
    // stamp-completion path still publishes reward.earned with source = stamp_completion (O1).
    public const string StampCompletion = "stamp_completion";
    public const string PointsPurchase = "points_purchase";
    public const string StreakCompletion = "streak_completion";

    /// <summary>The acquisitions a reward can be created with (CR 2026-09-30).</summary>
    public static readonly IReadOnlyList<string> Creatable = [PointsPurchase, StreakCompletion];

    public static bool IsCreatable(string acquisition) => Creatable.Contains(acquisition);
}
