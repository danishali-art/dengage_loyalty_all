using dEngage.Loyalty.RuleEngine.Models;

namespace dEngage.Loyalty.RuleEngine.Cache;

public interface IRuleCacheService
{
    Task<List<CachedRule>> GetRulesAsync(string tenantId, Guid programId, CancellationToken ct = default);
    Task<List<CachedRule>> LoadFromDbAsync(string tenantId, Guid programId, CancellationToken ct = default);
    Task InvalidateAsync(string tenantId, Guid programId);
}
