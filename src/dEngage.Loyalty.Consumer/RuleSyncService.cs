using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Consumer;

// Rule cache invalidation model
// ─────────────────────────────
//   Delta sync (30 sec) — INSERT / UPDATE / soft-delete (UpdatedAt is bumped)
//   Full sync  (1 hour) — defensive safety net: clock skew, manual bulk updates,
//                         scenarios like DR restore that break UpdatedAt
//   Trigger (DB)         — turns a physical DELETE into status='deleted' + updated_at=NOW()
//
// Concurrency: only one sync runs at a time. If a full sync runs long, delta syncs
// waking up meanwhile fail to acquire the semaphore and skip — no double work, no double Redis writes.
public class RuleSyncService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<RuleSyncService> logger) : BackgroundService
{
    private readonly TaskCompletionSource _ready = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private DateTime _lastSync     = DateTime.MinValue;
    private DateTime _lastFullSync = DateTime.MinValue;

    private static readonly TimeSpan DeltaInterval    = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FullSyncInterval = TimeSpan.FromHours(1);

    // 2+4+8+16+32 = 62s total wait. Then fail fast.
    private const int MaxInitialLoadAttempts = 5;

    public Task WaitUntilReadyAsync() => _ready.Task;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("RuleSync: initial load starting");

        // Bounded exponential backoff if the initial load fails. After max attempts,
        // bring the process down — let the orchestrator (K8s/Docker) restart it.
        var loaded = false;
        for (var attempt = 1; attempt <= MaxInitialLoadAttempts; attempt++)
        {
            if (ct.IsCancellationRequested) return;

            try
            {
                await LoadAllRulesAsync(ct);
                loaded = true;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt == MaxInitialLoadAttempts)
                {
                    logger.LogCritical(ex,
                        "RuleSync: initial load failed after {Max} attempts — shutting down for orchestrator restart",
                        MaxInitialLoadAttempts);
                    _ready.TrySetException(new InvalidOperationException(
                        $"RuleSync initial load failed after {MaxInitialLoadAttempts} attempts", ex));
                    lifetime.StopApplication();
                    return;
                }

                var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogError(ex,
                    "RuleSync: initial load failed (attempt {Attempt}/{Max}), retry in {Backoff}s",
                    attempt, MaxInitialLoadAttempts, backoff.TotalSeconds);
                await Task.Delay(backoff, ct);
            }
        }

        if (!loaded) return;

        _ready.TrySetResult();
        _lastFullSync = DateTime.UtcNow;
        logger.LogInformation("RuleSync: ready, consumer can start");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DeltaInterval, ct);
                if (ct.IsCancellationRequested) break;

                if (DateTime.UtcNow - _lastFullSync >= FullSyncInterval)
                    await TryRunAsync("full", () => LoadAllRulesAsync(ct), isFull: true, ct);
                else
                    await TryRunAsync("delta", () => SyncChangedRulesAsync(ct), isFull: false, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RuleSync: iteration failed, will retry");
            }
        }
    }

    // Try to take the semaphore for this tick only. If unavailable (another sync is
    // in progress) skip — retry on the next 30s tick. No double work, double Redis
    // writes, or double DB reads.
    private async Task TryRunAsync(string kind, Func<Task> action, bool isFull, CancellationToken ct)
    {
        if (!await _syncLock.WaitAsync(TimeSpan.Zero, ct))
        {
            logger.LogDebug("RuleSync: {Kind} skipped — another sync in progress", kind);
            return;
        }

        try
        {
            await action();
            if (isFull) _lastFullSync = DateTime.UtcNow;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    // Full load at startup / after a crash / periodically.
    // Rebuilds the cache from scratch for active programs.
    private async Task LoadAllRulesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();
        var ruleCache = scope.ServiceProvider.GetRequiredService<IRuleCacheService>();
        var campaignConfigCache = scope.ServiceProvider.GetRequiredService<ICampaignConfigCacheService>();
        var tenantSlugResolver = scope.ServiceProvider.GetRequiredService<ITenantSlugResolver>();

        var programs = await db.Programs
            // 1.3.CL item 8: only published + active programs run — a draft is never evaluated.
            .Where(p => p.Status == ProgramStatus.Active && p.PublicationStatus == ProgramPublicationStatus.Published)
            .Select(p => new { p.TenantId, p.Id })
            .ToListAsync(ct);

        foreach (var p in programs)
        {
            // Atomic update: overwrite the cache key with the new value.
            // We avoid a two-step Invalidate + Load because the millisecond window
            // in between would push the engine into a cache miss and an unnecessary DB round-trip.
            var tenantSlug = await tenantSlugResolver.ResolveSlugAsync(p.TenantId, ct);
            await ruleCache.LoadFromDbAsync(tenantSlug, p.Id, ct);
            await campaignConfigCache.LoadFromDbAsync(tenantSlug, p.Id, ct);
            logger.LogDebug("RuleSync: loaded rules for {Tenant}/{Program}", tenantSlug, p.Id);
        }

        _lastSync = DateTime.UtcNow;
    }

    // Delta sync. UpdatedAt-based — also catches soft-delete (status='deleted'|'disabled')
    // changes because the trigger updates UpdatedAt.
    private async Task SyncChangedRulesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();
        var ruleCache = scope.ServiceProvider.GetRequiredService<IRuleCacheService>();
        var campaignConfigCache = scope.ServiceProvider.GetRequiredService<ICampaignConfigCacheService>();
        var tenantSlugResolver = scope.ServiceProvider.GetRequiredService<ITenantSlugResolver>();

        var since = _lastSync.AddSeconds(-5); // clock skew allowance

        var changedRulePrograms = await db.Rules
            .IgnoreQueryFilters()
            .Where(r => r.UpdatedAt >= since)
            .Select(r => new { r.TenantId, r.ProgramId })
            .Distinct()
            .ToListAsync(ct);

        // Streak campaigns live in their own table now — a change there must also refresh the
        // campaign cache, or an edited campaign would only pick up on the next full sync (1hr SLA
        // instead of 30s).
        var changedCampaignPrograms = await db.StreakCampaigns
            .IgnoreQueryFilters()
            .Where(c => c.UpdatedAt >= since)
            .Select(c => new { c.TenantId, c.ProgramId })
            .Distinct()
            .ToListAsync(ct);

        var changedPrograms = changedRulePrograms.Concat(changedCampaignPrograms).Distinct();

        foreach (var p in changedPrograms)
        {
            // Atomic update — overwrite, don't invalidate (the race window disappears)
            var tenantSlug = await tenantSlugResolver.ResolveSlugAsync(p.TenantId, ct);
            await ruleCache.LoadFromDbAsync(tenantSlug, p.ProgramId, ct);
            await campaignConfigCache.LoadFromDbAsync(tenantSlug, p.ProgramId, ct);
            logger.LogInformation("RuleSync: refreshed rules for {Tenant}/{Program}", tenantSlug, p.ProgramId);
        }

        _lastSync = DateTime.UtcNow;
    }
}
