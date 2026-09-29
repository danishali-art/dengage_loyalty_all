namespace dEngage.Loyalty.RuleEngine.Cache;

public interface ILimitCacheService
{
    Task<decimal> GetTotalAsync(string tenantId, Guid ruleId, string contactKey, CancellationToken ct = default);
    Task<decimal> GetDailyAsync(string tenantId, Guid ruleId, string contactKey, CancellationToken ct = default);
    Task IncrementAsync(string tenantId, Guid ruleId, string contactKey, decimal delta);
    Task InvalidateAsync(string tenantId, Guid ruleId, string contactKey);
}
