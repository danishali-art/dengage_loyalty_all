using System.Text.Json;
using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace dEngage.Loyalty.RuleEngine.Campaigns;

// Streak's cache-side counterpart of RuleCacheService, deliberately kept as its own service
// with its own Redis key ("campaigns:" vs "rules:") — a rule-cache consumer never sees campaign
// configs and vice versa, matching the runtime separation (ICampaignModule vs IRuleTypeHandler).
public class CampaignConfigCacheService(
    IConnectionMultiplexer redis,
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver,
    ILogger<CampaignConfigCacheService> logger) : ICampaignConfigCacheService
{
    private readonly IDatabase _cache = redis.GetDatabase();

    private static string CampaignKey(string tenantId, Guid programId) =>
        $"campaigns:{tenantId}:{programId}";

    public async Task<List<CachedCampaignConfig>> GetConfigsAsync(string tenantId, Guid programId, CancellationToken ct = default)
    {
        var key = CampaignKey(tenantId, programId);
        var cached = await _cache.StringGetAsync(key);

        if (cached.HasValue)
        {
            try
            {
                return JsonSerializer.Deserialize<List<CachedCampaignConfig>>(cached!) ?? new List<CachedCampaignConfig>();
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "CampaignConfigCache: corrupt payload for {Tenant}/{Program}, forcing reload", tenantId, programId);
            }
        }

        return await LoadFromDbAsync(tenantId, programId, ct);
    }

    public async Task<List<CachedCampaignConfig>> LoadFromDbAsync(string tenantId, Guid programId, CancellationToken ct = default)
    {
        // Only streak exists today — a second campaign type would get its own dedicated table
        // too (see CachedCampaignConfig's remarks), not a shared polymorphic one.
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var rows = await db.StreakCampaigns
            .Where(c =>
                c.TenantId == tenantGuid &&
                c.ProgramId == programId &&
                c.Status == RuleStatus.Active)
            .ToListAsync(ct);

        var configs = new List<CachedCampaignConfig>();
        foreach (var c in rows)
        {
            try
            {
                var conditions = c.Conditions is null ? null
                    : JsonSerializer.Deserialize<List<ConditionClause>>(c.Conditions);
                ConditionDsl.Validate(conditions);

                var streak = StreakConfig.Parse(c.Config);

                configs.Add(new CachedCampaignConfig
                {
                    Id = c.Id,
                    ProgramId = c.ProgramId,
                    Name = c.Name,
                    CampaignType = CampaignTypes.Streak,
                    Trigger = c.Trigger,
                    Conditions = conditions,
                    TargetAccountTypeId = c.TargetAccountTypeId,
                    ActiveFrom = c.ActiveFrom,
                    ActiveTo = c.ActiveTo,
                    Streak = streak
                });
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // One bad campaign config must not take the whole program's cache down
                logger.LogError(ex, "CampaignConfigCache: [{Tenant}] campaign '{Name}' ({Id}) invalid — skipped", tenantId, c.Name, c.Id);
            }
        }

        var key = CampaignKey(tenantId, programId);
        await _cache.StringSetAsync(key, JsonSerializer.Serialize(configs), TimeSpan.FromHours(1));

        return configs;
    }

    public async Task InvalidateAsync(string tenantId, Guid programId)
    {
        await _cache.KeyDeleteAsync(CampaignKey(tenantId, programId));
    }
}
