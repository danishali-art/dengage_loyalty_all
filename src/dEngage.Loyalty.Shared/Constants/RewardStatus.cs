namespace dEngage.Loyalty.Shared;

// CR 2026-09-30 (A4): approval state of a reward definition, orthogonal to IsActive. A cashback
// reward pays real money, so — like a CASH-targeted rule (CR-04, RuleStatus.PendingApproval) — it
// is created pending and a different admin than its creator must approve it before it can be
// bought or earned. Consumers only fulfil rewards that are IsActive AND Active here.
public static class RewardStatus
{
    public const string Active = "active";
    public const string PendingApproval = "pending_approval";
}
