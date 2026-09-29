using System.Text.Json;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace dEngage.Loyalty.RuleEngine.Cache;

public class RuleCacheService(
    IConnectionMultiplexer redis,
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver,
    ILogger<RuleCacheService> logger) : IRuleCacheService
{
    private readonly IDatabase _cache = redis.GetDatabase();

    private static string RuleKey(string tenantId, Guid programId) =>
        $"rules:{tenantId}:{programId}";

    public async Task<List<CachedRule>> GetRulesAsync(string tenantId, Guid programId, CancellationToken ct = default)
    {
        var key = RuleKey(tenantId, programId);
        var cached = await _cache.StringGetAsync(key);

        if (cached.HasValue)
        {
            try
            {
                return JsonSerializer.Deserialize<List<CachedRule>>(cached!) ?? new List<CachedRule>();
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "RuleCache: corrupt payload for {Tenant}/{Program}, forcing reload", tenantId, programId);
            }
        }

        return await LoadFromDbAsync(tenantId, programId, ct);
    }

    public async Task<List<CachedRule>> LoadFromDbAsync(string tenantId, Guid programId, CancellationToken ct = default)
    {
        // No active_from/to filter here — the window is checked at evaluation time,
        // so hour-precision windows work without waiting for a cache refresh.
        // Streak campaigns live in their own table entirely (see CampaignConfigCacheService) —
        // no rows in `rules` can be a streak campaign anymore, nothing to exclude here.
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var rules = await db.Rules
            .Where(r =>
                r.TenantId == tenantGuid &&
                r.ProgramId == programId &&
                r.Status == RuleStatus.Active)
            .ToListAsync(ct);

        // 1.3.CL item 2: each rule carries its target wallet's decimals so Spend rules can round
        // to it without a per-event account-type lookup.
        var decimalsByAccountType = (await db.AccountTypes
                .AsNoTracking()
                .Where(a => a.TenantId == tenantGuid && a.ProgramId == programId)
                .Select(a => new { a.Id, a.Config })
                .ToListAsync(ct))
            .ToDictionary(a => a.Id, a => ReadDecimals(a.Config));

        var cached = new List<CachedRule>();
        var backfilledAny = false;
        foreach (var r in rules)
        {
            try
            {
                // CR-05 read-repair: a row written before this branch's grouped-condition
                // migration still holds the old flat `[{field,op,value}]` shape (a JSON array at
                // the root) instead of the new `{op,groups}` tree (a JSON object;
                // FlatConditionsMigrator.ParseConditions detects and converts either). There is
                // no separate deploy-time backfill step to remember to run — converting once
                // here and persisting the result back onto the row means every tenant's
                // pre-existing rules keep matching without an operator action, and this branch
                // only ever pays the conversion cost once per row.
                var conditions = FlatConditionsMigrator.ParseConditions(r.Conditions, out var wasFlat);
                if (wasFlat)
                {
                    r.Conditions = conditions is null ? null : JsonSerializer.Serialize(conditions);
                    backfilledAny = true;
                    logger.LogWarning(
                        "RuleCache: [{Tenant}] rule '{Rule}' ({Id}) had pre-CR-05 flat conditions — backfilled to the grouped shape",
                        tenantId, r.Name, r.Id);
                }
                GroupedConditionDsl.Validate(conditions);

                cached.Add(new CachedRule
                {
                    Id = r.Id,
                    ProgramId = r.ProgramId,
                    Name = r.Name,
                    Type = r.Type,
                    Trigger = r.Trigger,
                    Conditions = conditions,
                    Calculation = JsonSerializer.Deserialize<RuleCalculation>(r.Calculation)!,
                    TargetAccountTypeId = r.TargetAccountTypeId,
                    TargetDecimals = r.TargetAccountTypeId is { } targetId
                        ? decimalsByAccountType.GetValueOrDefault(targetId) : 0,
                    Limits = r.Limits is null ? null
                        : JsonSerializer.Deserialize<RuleLimits>(r.Limits),
                    Configuration = r.Configuration is null ? null
                        : JsonSerializer.Deserialize<RuleSettings>(r.Configuration),
                    Priority = r.Priority,
                    Stackable = r.Stackable,
                    ExclusivityGroup = r.ExclusivityGroup,
                    StackMode = r.StackMode,
                    ActiveFrom = r.ActiveFrom,
                    ActiveTo = r.ActiveTo,
                    Version = r.CurrentVersion
                });
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // One bad rule must not take the whole program's cache down
                logger.LogError(ex, "RuleCache: [{Tenant}] rule '{Rule}' ({Id}) invalid — skipped", tenantId, r.Name, r.Id);
            }
        }

        if (backfilledAny)
        {
            await db.SaveChangesAsync(ct);
        }

        var key = RuleKey(tenantId, programId);
        // Flat safety-net TTL — freshness comes from RuleSync (30s delta); active windows
        // are enforced at evaluation time, so midnight-precise expiry is no longer needed.
        await _cache.StringSetAsync(key, JsonSerializer.Serialize(cached), TimeSpan.FromHours(1));

        return cached;
    }

    public async Task InvalidateAsync(string tenantId, Guid programId)
    {
        await _cache.KeyDeleteAsync(RuleKey(tenantId, programId));
    }

    // A missing/non-integer value means 0 places — the pre-1.3.CL whole-number behaviour.
    private static int ReadDecimals(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            return doc.RootElement.TryGetProperty("decimals", out var v) && v.TryGetInt32(out var places)
                ? Math.Clamp(places, 0, 4) : 0;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return 0;
        }
    }
}
