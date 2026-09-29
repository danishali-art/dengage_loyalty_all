namespace dEngage.Loyalty.Shared;

public static class RuleStatus
{
    public const string Active   = "active";
    public const string Disabled = "disabled";
    public const string Deleted  = "deleted";

    // CR-04: CASH-targeted rules land here instead of Active on creation — a distinct admin
    // must approve before they can match (RuleCacheService only loads Active rows, so a
    // PendingApproval rule cannot fire). See RulesAppService.CreateAsync/ApproveAsync.
    public const string PendingApproval = "pending_approval";
}
