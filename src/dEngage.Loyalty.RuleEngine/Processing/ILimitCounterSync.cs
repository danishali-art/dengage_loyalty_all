namespace dEngage.Loyalty.RuleEngine.Processing;

public interface ILimitCounterSync
{
    Task SyncAsync(string tenantId, string contactKey, IReadOnlyList<AppliedRule> appliedRules);
}
