namespace dEngage.Loyalty.RuleEngine.Campaigns;

public interface ICampaignConfigCacheService
{
    Task<List<CachedCampaignConfig>> GetConfigsAsync(string tenantId, Guid programId, CancellationToken ct = default);
    Task<List<CachedCampaignConfig>> LoadFromDbAsync(string tenantId, Guid programId, CancellationToken ct = default);
    Task InvalidateAsync(string tenantId, Guid programId);
}
