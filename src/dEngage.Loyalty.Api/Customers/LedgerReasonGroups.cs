using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Api.Customers;

// CR 2026-10-02 (Customer 360, §3.6): the Activity tab's "reason group" filter. Every
// LedgerReason value belongs to exactly one group — add a new reason here when it is added there.
internal static class LedgerReasonGroups
{
    public static readonly IReadOnlyDictionary<string, string[]> Groups = new Dictionary<string, string[]>
    {
        ["earn"] = [LedgerReason.Earn, LedgerReason.StampEarn, LedgerReason.CashLoad],
        ["burn"] = [LedgerReason.PointsRedeemed, LedgerReason.PointsRedeemedCash, LedgerReason.RewardPurchase,
            LedgerReason.CashSpend, LedgerReason.StampReset],
        ["transfer"] = [LedgerReason.TransferOut, LedgerReason.TransferIn],
        ["expiry"] = [LedgerReason.PointsExpired],
        ["reward"] = [LedgerReason.RewardCashback],
        ["adjustment"] = [LedgerReason.PointsAdjusted, LedgerReason.Refund, LedgerReason.RuleReversal],
    };
}
