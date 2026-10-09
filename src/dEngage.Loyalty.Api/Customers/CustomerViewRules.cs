using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Api.Customers;

// CR 2026-10-02 (Customer 360) P2: read-side copies of rules the engine applies, so the customer
// view shows the same figures. Keep them in step with the code each one names.
internal static class CustomerViewRules
{
    // PointsExpiringDetectorJob.DetectSql, for one account (CR 2026-10-06 Phase 5): each earn /
    // transfer_in is a lot dated ExpiresAt, or created_at + expiration_days when it has none;
    // consumption is taken from the soonest-expiring lots first (ties by created_at, then id). What
    // is left of lots dated on or before today + warning_days is "expiring soon", capped by the
    // balance, and dated by the earliest such lot. With no expiry override this is the old
    // oldest-first FIFO. Ties on created_at are ordered by id here and by uuid in Postgres — only
    // equal timestamps could order differently.
    private static readonly string[] ExpiryEarnReasons = [LedgerReason.Earn, LedgerReason.TransferIn];
    public static readonly string[] ExpiryConsumptionReasons =
    [
        LedgerReason.PointsRedeemed, LedgerReason.PointsRedeemedCash, LedgerReason.Refund,
        LedgerReason.PointsExpired, LedgerReason.RewardPurchase, LedgerReason.TransferOut
    ];
    public static readonly string[] ExpiryReasons = [.. ExpiryEarnReasons, .. ExpiryConsumptionReasons];

    // ExpiresAt: CR 2026-10-06 Phase 5 (ledger_entries.expires_at); null on entries written before it.
    public sealed record ExpiryEntry(Guid Id, string Reason, decimal Delta, DateTime CreatedAt, DateTime? ExpiresAt = null);

    public static (decimal Amount, DateOnly ExpiresOn)? ExpiringSoon(
        decimal balance, int expirationDays, int warningDays, IEnumerable<ExpiryEntry> entries, DateTime utcNow)
    {
        // The job skips a wallet whose warning_days isn't 0 < warning_days < expiration_days.
        if (balance <= 0 || warningDays <= 0 || warningDays >= expirationDays)
            return null;

        var list = entries.ToList();
        var warnBefore = utcNow.Date.AddDays(warningDays);
        var consumed = Math.Abs(list.Where(e => ExpiryConsumptionReasons.Contains(e.Reason)).Sum(e => e.Delta));

        decimal cumulativeBefore = 0, expirable = 0;
        DateTime? firstExpiring = null;
        var lots = list.Where(e => ExpiryEarnReasons.Contains(e.Reason))
            .Select(e => (Entry: e, Expires: e.ExpiresAt ?? e.CreatedAt.AddDays(expirationDays)))
            .OrderBy(l => l.Expires).ThenBy(l => l.Entry.CreatedAt).ThenBy(l => l.Entry.Id);
        foreach (var (e, expires) in lots)
        {
            if (expires <= warnBefore)
            {
                var remaining = Math.Max(0, e.Delta - Math.Max(0, consumed - cumulativeBefore));
                if (remaining > 0)
                {
                    expirable += remaining;
                    firstExpiring ??= expires;
                }
            }
            cumulativeBefore += e.Delta;
        }

        return expirable > 0 && firstExpiring is { } first
            ? (Math.Min(expirable, balance), DateOnly.FromDateTime(first))
            : null;
    }

    // RuleLimitEvaluator / LimitCacheService count these reasons towards per-customer caps.
    public static readonly string[] CapReasons = [LedgerReason.Earn, LedgerReason.StampEarn, LedgerReason.Refund];

    // tier_upgrade_log.source_event_id: TierDowngradeJob writes "downgrade:{account}:{date}",
    // RewardFulfilmentService "{grant}:tier_upgrade", TierEvaluationService the inbound event id.
    public const string CausePoints = "points";
    public const string CauseReward = "reward";
    public const string CauseDowngrade = "downgrade";

    public static string TierChangeCause(string sourceEventId) =>
        sourceEventId.StartsWith("downgrade:", StringComparison.Ordinal) ? CauseDowngrade
        : sourceEventId.EndsWith(":tier_upgrade", StringComparison.Ordinal) ? CauseReward
        : CausePoints;

    // RewardFulfilmentService keys a payout on its grant: the purchase event id for a reward
    // bought with points (RewardPurchaseHandler), "streak:{campaign}:{contact}:{completion}" for a
    // streak (StreakCampaignModule).
    public static string StreakGrant(Guid campaignId, string contactKey, int completionNo) =>
        $"streak:{campaignId}:{contactKey}:{completionNo}";

    public static string CashbackKey(string grant) => $"{grant}:reward_cashback";
    public static string TierUpgradeKey(string grant) => $"{grant}:tier_upgrade";
}
