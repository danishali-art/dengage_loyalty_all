using dEngage.Loyalty.Schema.Entities;

namespace dEngage.Loyalty.RuleEngine.Processing;

// CR 2026-09-30 (A2, §3.5/§3.6): the one seam that acts on a reward's RewardType + TypeConfig, so
// RewardPurchaseHandler and StreakCampaignModule don't each branch on reward types. Runs inside the
// caller's transaction and only stages changes — the caller saves and commits, so the reward's
// payout commits atomically with whatever earned or bought it.
public interface IRewardFulfilmentService
{
    /// <summary>
    /// Cashback is only paid for a published + active program (O10). Callers that take something
    /// in exchange (a points purchase) check this first, so nothing is debited for a payout that
    /// will not happen.
    /// </summary>
    Task<bool> IsProgramLiveAsync(Guid tenantGuid, Guid programId, CancellationToken ct);

    /// <param name="idempotencyBase">
    /// Deterministic per grant (e.g. the purchase event id, or streak:{campaign}:{contact}:{n});
    /// every posting / log / outbox row this grant writes derives its key from it.
    /// </param>
    Task<RewardFulfilment> FulfilAsync(
        string tenantSlug,
        Guid tenantGuid,
        RewardDefinition reward,
        string contactKey,
        string sourceEventId,
        string idempotencyBase,
        CancellationToken ct);
}

/// <summary>What a grant did — merged into the outbound loyalty.reward.earned payload (snake_case).</summary>
public sealed record RewardFulfilment(string Outcome, IReadOnlyDictionary<string, object?> EventFields, string? Reference);

public static class RewardFulfilmentOutcome
{
    public const string CashCredited = "cash_credited";
    public const string TierUpgraded = "tier_upgraded";
    public const string AlreadyAtOrAbove = "already_at_or_above";
    public const string ProgramNotLive = "program_not_live";
    public const string NotFulfillable = "not_fulfillable";
}
