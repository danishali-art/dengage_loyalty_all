using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Campaigns.Streak;

// Nightly, per campaign: recompute (late-event correction) → break detection → 13-month purge.
// Recompute runs BEFORE break so a late event that filled yesterday does not
// produce a false break the same night. A break notification that already went
// out on a previous night is never recalled (documented v1 behavior).
public class StreakMaintenanceJob(
    LoyaltyDbContext db,
    IStreakCampaignModule streakModule,
    ILogger<StreakMaintenanceJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        logger.LogInformation("StreakMaintenance: started at {Now:O}", nowUtc);

        var campaigns = await db.StreakCampaigns
            .AsNoTracking()
            .Where(c => c.Status == RuleStatus.Active)
            .ToListAsync(ct);

        foreach (var campaign in campaigns)
        {
            try
            {
                await ProcessCampaignAsync(campaign, nowUtc, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "StreakMaintenance: [{Tenant}] campaign '{Campaign}' ({Id}) failed — continuing",
                    campaign.TenantId, campaign.Name, campaign.Id);
            }
        }

        await PurgeAsync(nowUtc, ct);

        logger.LogInformation("StreakMaintenance: completed ({Campaigns} campaigns)", campaigns.Count);
    }

    private async Task ProcessCampaignAsync(StreakCampaign campaign, DateTime nowUtc, CancellationToken ct)
    {
        StreakConfig config;
        try
        {
            config = StreakConfig.Parse(campaign.Config);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            logger.LogError(ex, "StreakMaintenance: [{Tenant}] campaign '{Campaign}' invalid config — skipped",
                campaign.TenantId, campaign.Name);
            return;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(config.Timezone);
        var currentPeriod = PeriodCalculator.GetPeriodStart(nowUtc, tz, config.Period, config.WeekStart);
        var prevPeriod = PeriodCalculator.PreviousPeriodStart(currentPeriod, config.Period);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var (fixedCount, completions) = await RecomputeAsync(campaign, config, currentPeriod, prevPeriod, nowUtc, ct);
        var broken = await BreakAsync(campaign, prevPeriod, ct);

        await tx.CommitAsync(ct);

        if (fixedCount > 0 || completions > 0 || broken > 0)
            logger.LogInformation(
                "StreakMaintenance: [{Tenant}] campaign '{Campaign}' — recomputed={Fixed} completions={Completions} broken={Broken}",
                campaign.TenantId, campaign.Name, fixedCount, completions, broken);
    }

    // Rebuild streak_count from the met period rows in the last target_periods window.
    // Corrects counts skewed by late events; triggers a missed completion if one is due
    // (streak_log's unique index is the last guard against a double reward).
    private async Task<(int Fixed, int Completions)> RecomputeAsync(
        StreakCampaign campaign,
        StreakConfig config,
        DateOnly currentPeriod,
        DateOnly prevPeriod,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var progresses = await db.StreakProgresses
            .Where(p => p.TenantId == campaign.TenantId &&
                        p.CampaignId == campaign.Id &&
                        p.Status == StreakProgressStatus.Active)
            .ToListAsync(ct);
        if (progresses.Count == 0) return (0, 0);

        var windowFloor = currentPeriod;
        for (var i = 0; i < config.TargetPeriods; i++)
            windowFloor = PeriodCalculator.PreviousPeriodStart(windowFloor, config.Period);

        var metStates = await db.StreakPeriodStates
            .AsNoTracking()
            .Where(s => s.TenantId == campaign.TenantId &&
                        s.CampaignId == campaign.Id &&
                        s.Met &&
                        s.PeriodStart >= windowFloor)
            .Select(s => new { s.ContactKey, s.PeriodStart })
            .ToListAsync(ct);

        var metByContact = metStates
            .GroupBy(x => x.ContactKey)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PeriodStart).ToHashSet());

        // Periods consumed by an earlier completion must not seed a new streak
        var lastCompleted = await db.StreakLogs
            .AsNoTracking()
            .Where(l => l.TenantId == campaign.TenantId && l.CampaignId == campaign.Id)
            .GroupBy(l => l.ContactKey)
            .Select(g => new { ContactKey = g.Key, Last = g.Max(l => l.CompletedPeriod) })
            .ToDictionaryAsync(x => x.ContactKey, x => x.Last, ct);

        var cachedConfig = new CachedCampaignConfig
        {
            Id = campaign.Id,
            ProgramId = campaign.ProgramId,
            Name = campaign.Name,
            CampaignType = CampaignTypes.Streak,
            Trigger = campaign.Trigger,
            TargetAccountTypeId = campaign.TargetAccountTypeId,
            Streak = config
        };

        var fixedCount = 0;
        var completions = 0;
        var sourceEventId = $"streak_maintenance:{DateOnly.FromDateTime(nowUtc):O}";

        foreach (var progress in progresses)
        {
            if (!metByContact.TryGetValue(progress.ContactKey, out var mets) || mets.Count == 0)
                continue;

            var maxMet = mets.Max();
            if (maxMet < prevPeriod) continue; // stale — the break step handles it

            DateOnly? floor = lastCompleted.TryGetValue(progress.ContactKey, out var lc) ? lc : null;

            var count = 0;
            var cursor = maxMet;
            while (mets.Contains(cursor) && (floor is null || cursor > floor.Value))
            {
                count++;
                if (count >= config.TargetPeriods) break;
                cursor = PeriodCalculator.PreviousPeriodStart(cursor, config.Period);
            }

            if (count == progress.StreakCount && maxMet == progress.LastMetPeriod)
                continue;

            logger.LogInformation(
                "StreakMaintenance: [{Tenant}] campaign '{Campaign}' contact={Contact} recompute {Old}→{New} (last_met {OldLast}→{NewLast})",
                campaign.TenantId, campaign.Name, progress.ContactKey,
                progress.StreakCount, count, progress.LastMetPeriod, maxMet);

            progress.StreakCount = count;
            progress.LastMetPeriod = maxMet;
            progress.UpdatedAt = nowUtc;
            fixedCount++;

            if (count >= config.TargetPeriods)
            {
                await streakModule.CompleteAsync(campaign.TenantId, sourceEventId, cachedConfig, progress, maxMet, nowUtc, ct);
                completions++;
            }
        }

        await db.SaveChangesAsync(ct);
        return (fixedCount, completions);
    }

    // Active streaks whose last met period is older than the just-ended period are broken:
    // outbox event first (dedup key makes reruns safe), then a set-based counter reset.
    private async Task<int> BreakAsync(StreakCampaign campaign, DateOnly prevPeriod, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO outbox_events (
                event_id, tenant_id, event_type, contact_key,
                payload, dedup_key, status, attempts, next_attempt_at, created_at
            )
            SELECT
                x.event_id,
                x.tenant_id,
                'loyalty.streak.broken',
                x.contact_key,
                jsonb_build_object(
                    'eventId',    x.event_id::text,
                    'eventType',  'loyalty.streak.broken',
                    'tenant',     x.tenant_id,
                    'occurredAt', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
                    'version',    '1',
                    'data', jsonb_build_object(
                        'contact_key',     x.contact_key,
                        'rule_id',         x.campaign_id,
                        'rule_name',       {campaign.Name},
                        'broken_period',   x.broken_period,
                        'last_met_period', x.last_met,
                        'streak_count',    x.streak_count
                    )
                ),
                x.dedup_key,
                {OutboxStatus.Pending}, 0, NOW(), NOW()
            FROM (
                SELECT
                    gen_random_uuid() AS event_id,
                    sp.tenant_id,
                    sp.contact_key,
                    sp.campaign_id::text AS campaign_id,
                    to_char({prevPeriod}, 'YYYY-MM-DD') AS broken_period,
                    to_char(sp.last_met_period, 'YYYY-MM-DD') AS last_met,
                    sp.streak_count,
                    'streak_broken:' || sp.campaign_id::text || ':' || sp.contact_key || ':' || to_char({prevPeriod}, 'YYYY-MM-DD') AS dedup_key
                FROM streak_progress sp
                WHERE sp.tenant_id = {campaign.TenantId}
                  AND sp.campaign_id = {campaign.Id}
                  AND sp.status = {StreakProgressStatus.Active}
                  AND sp.streak_count > 0
                  AND sp.last_met_period < {prevPeriod}
            ) x
            ON CONFLICT (tenant_id, dedup_key) WHERE dedup_key IS NOT NULL DO NOTHING
            """, ct);

        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE streak_progress sp
            SET streak_count = 0, updated_at = NOW()
            WHERE sp.tenant_id = {campaign.TenantId}
              AND sp.campaign_id = {campaign.Id}
              AND sp.status = {StreakProgressStatus.Active}
              AND sp.streak_count > 0
              AND sp.last_met_period < {prevPeriod}
            """, ct);
    }

    // event_log/period_state retention is 12 months — the safety margin gives the nightly
    // recompute a buffer beyond that window.
    private async Task PurgeAsync(DateTime nowUtc, CancellationToken ct)
    {
        var cutoffMonths = -(RetentionPolicy.EventRetentionMonths + RetentionPolicy.PurgeSafetyMarginMonths);
        var cutoffDate = DateOnly.FromDateTime(nowUtc).AddMonths(cutoffMonths);
        var cutoffTs = nowUtc.AddMonths(cutoffMonths);

        var states = await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM streak_period_state WHERE period_start < {cutoffDate}", ct);
        var applied = await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM streak_applied_event WHERE applied_at < {cutoffTs}", ct);

        if (states > 0 || applied > 0)
            logger.LogInformation("StreakMaintenance: purged {States} period states, {Applied} applied events", states, applied);
    }
}
