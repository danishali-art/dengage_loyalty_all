using System.Text.Json;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Campaigns.Streak;

public class StreakCampaignModule(
    LoyaltyDbContext db,
    ILedgerService ledger,
    IOutboxService outbox,
    ITenantSlugResolver tenantSlugResolver,
    ILogger<StreakCampaignModule> logger) : IStreakCampaignModule
{
    public string CampaignType => CampaignTypes.Streak;

    // Caller (RuleEngine) has already checked trigger, active window and conditions.
    // Earned=true if a ledger earn happened (completion with fixed_bonus) so the
    // caller can trigger tier re-evaluation.
    public async Task<CampaignOutcome> EvaluateAsync(CampaignEvaluationRequest request, CancellationToken ct)
    {
        var (tenantId, eventId, config, evt) = request;
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        var streakConfig = config.Streak!;
        var earned = false;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Idempotency anchor — event_log's rowcount cannot be relied on (it is inserted
        // before dispatch in a separate step), so the streak keeps its own applied set.
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO streak_applied_event (tenant_id, campaign_id, event_id, applied_at)
            VALUES ({tenantGuid}, {config.Id}, {eventId}, {DateTime.UtcNow})
            ON CONFLICT DO NOTHING
            """, ct);
        if (inserted == 0)
        {
            logger.LogInformation(
                "Streak: [{Tenant}] rule [{Rule}] event {EventId} already applied — skipped",
                tenantId, config.Name, eventId);
            return CampaignOutcome.NotEarned;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(streakConfig.Timezone);
        var periodStart = PeriodCalculator.GetPeriodStart(evt.OccurredAt, tz, streakConfig.Period, streakConfig.WeekStart);
        var now = DateTime.UtcNow;

        // Consumer is sequential (BasicQos 0,1) — plain read-modify-write is safe here.
        var state = await db.StreakPeriodStates.FirstOrDefaultAsync(s =>
            s.TenantId == tenantGuid &&
            s.CampaignId == config.Id &&
            s.ContactKey == evt.ContactKey &&
            s.PeriodStart == periodStart, ct);
        if (state is null)
        {
            state = new StreakPeriodState
            {
                TenantId = tenantGuid,
                CampaignId = config.Id,
                ContactKey = evt.ContactKey,
                PeriodStart = periodStart
            };
            db.StreakPeriodStates.Add(state);
        }

        state.AggSum += evt.Amount;
        state.AggCount += 1;
        state.UpdatedAt = now;

        var metric = streakConfig.Aggregate.Metric == StreakMetric.Sum ? state.AggSum : state.AggCount;
        var nowMet = metric >= streakConfig.Aggregate.Threshold;
        var transition = !state.Met && nowMet;
        state.Met = nowMet;

        if (transition)
            earned = await AdvanceProgressAsync(tenantGuid, eventId, config, streakConfig, evt.ContactKey, periodStart, now, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new CampaignOutcome(earned);
    }

    private async Task<bool> AdvanceProgressAsync(
        Guid tenantId,
        string eventId,
        CachedCampaignConfig config,
        StreakConfig streakConfig,
        string contactKey,
        DateOnly periodStart,
        DateTime now,
        CancellationToken ct)
    {
        var progress = await db.StreakProgresses.FirstOrDefaultAsync(p =>
            p.TenantId == tenantId &&
            p.CampaignId == config.Id &&
            p.ContactKey == contactKey, ct);
        if (progress is null)
        {
            progress = new StreakProgress
            {
                TenantId = tenantId,
                CampaignId = config.Id,
                ContactKey = contactKey,
                Status = StreakProgressStatus.Active
            };
            db.StreakProgresses.Add(progress);
        }

        if (progress.Status == StreakProgressStatus.CompletedStopped)
            return false;

        // Late event filling an older (or same) period cannot be advanced incrementally —
        // the nightly recompute rebuilds streak_count from period_state rows.
        if (progress.LastMetPeriod is not null && periodStart <= progress.LastMetPeriod)
        {
            logger.LogInformation(
                "Streak: [{Tenant}] rule [{Rule}] contact={Contact} late met for {Period} (last={Last}) — deferred to recompute",
                tenantId, config.Name, contactKey, periodStart, progress.LastMetPeriod);
            return false;
        }

        var previous = PeriodCalculator.PreviousPeriodStart(periodStart, streakConfig.Period);
        progress.StreakCount = progress.LastMetPeriod == previous ? progress.StreakCount + 1 : 1;
        progress.LastMetPeriod = periodStart;
        progress.UpdatedAt = now;

        logger.LogInformation(
            "Streak: [{Tenant}] rule [{Rule}] contact={Contact} period={Period} met — streak {Count}/{Target}",
            tenantId, config.Name, contactKey, periodStart, progress.StreakCount, streakConfig.TargetPeriods);

        if (progress.StreakCount < streakConfig.TargetPeriods)
            return false;

        return await CompleteAsync(tenantId, eventId, config, progress, periodStart, now, ct);
    }

    // Also called by StreakMaintenanceJob when a recompute uncovers a missed completion.
    // Does NOT SaveChanges — the caller's transaction owns persistence.
    public async Task<bool> CompleteAsync(
        Guid tenantId,
        string eventId,
        CachedCampaignConfig config,
        StreakProgress progress,
        DateOnly completedPeriod,
        DateTime now,
        CancellationToken ct)
    {
        var streakConfig = config.Streak!;
        var completionNo = progress.Completions + 1;
        var earned = false;
        string? rewardRef = null;

        // ledger/outbox take the external slug (RabbitMQ envelopes, ledger tenant_id partition
        // key) — resolved back from the Guid this method otherwise operates in.
        var tenantSlug = await tenantSlugResolver.ResolveSlugAsync(tenantId, ct);

        if (streakConfig.Reward.Kind == StreakRewardKind.FixedBonus)
        {
            var account = await ledger.UpsertAccountAsync(tenantSlug, progress.ContactKey, config.TargetAccountTypeId, ct);
            var entry = await ledger.AddEntryAsync(
                tenantSlug, account.Id, progress.ContactKey,
                streakConfig.Reward.Amount!.Value, LedgerReason.Earn, eventId,
                idempotencyKey: $"streak:{config.Id}:{progress.ContactKey}:{completionNo}",
                ruleId: config.Id,
                metadata: JsonSerializer.Serialize(new { streak_completion_no = completionNo, completed_period = completedPeriod.ToString("O") }),
                ct: ct);
            rewardRef = entry.Id.ToString();
            earned = true;
        }
        else // reward_definition
        {
            var definition = await db.RewardDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.TenantId == tenantId &&
                    x.Id == streakConfig.Reward.RewardDefinitionId!.Value &&
                    x.IsActive, ct);

            if (definition is not null)
            {
                rewardRef = definition.Id.ToString();
                await outbox.Enqueue(
                    tenantSlug,
                    dEngage.Loyalty.Shared.Events.OutboundEventTypes.RewardEarned,
                    progress.ContactKey,
                    new
                    {
                        contact_key = progress.ContactKey,
                        reward_name = definition.Name,
                        reward_type = definition.RewardType,
                        source = RewardAcquisition.StreakCompletion,
                        completion_count = completionNo,
                        source_event_id = eventId
                    },
                    dedupKey: $"reward_earned:streak:{config.Id}:{progress.ContactKey}:{completionNo}");
            }
            else
            {
                // Complete the streak anyway — the reward stays traceable via streak_log
                logger.LogWarning(
                    "Streak: [{Tenant}] rule [{Rule}] completed but reward_definition {DefId} not found/inactive — no reward event",
                    tenantId, config.Name, streakConfig.Reward.RewardDefinitionId);
            }
        }

        // Unique index (tenant, rule, contact, completion_no) is the last line of defense
        // against a double reward if progress state ever diverges.
        db.StreakLogs.Add(new StreakLog
        {
            Id = UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql),
            TenantId = tenantId,
            CampaignId = config.Id,
            ContactKey = progress.ContactKey,
            CompletionNo = completionNo,
            CompletedPeriod = completedPeriod,
            Periods = streakConfig.TargetPeriods,
            RewardKind = streakConfig.Reward.Kind,
            RewardRef = rewardRef,
            SourceEventId = eventId,
            CreatedAt = now
        });

        await outbox.Enqueue(
            tenantSlug,
            dEngage.Loyalty.Shared.Events.OutboundEventTypes.StreakCompleted,
            progress.ContactKey,
            new
            {
                contact_key = progress.ContactKey,
                rule_id = config.Id.ToString(),
                rule_name = config.Name,
                completion_no = completionNo,
                periods = streakConfig.TargetPeriods,
                period = streakConfig.Period,
                completed_period = completedPeriod.ToString("O"),
                reward_kind = streakConfig.Reward.Kind,
                reward_amount = streakConfig.Reward.Amount?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                reward_definition_id = streakConfig.Reward.RewardDefinitionId?.ToString(),
                source_event_id = eventId
            },
            dedupKey: $"streak_completed:{config.Id}:{progress.ContactKey}:{completionNo}");

        progress.Completions = completionNo;
        progress.StreakCount = 0;
        if (streakConfig.OnComplete == StreakOnComplete.Stop)
            progress.Status = StreakProgressStatus.CompletedStopped;

        logger.LogInformation(
            "Streak: [{Tenant}] COMPLETED rule [{Rule}] contact={Contact} completion={No} reward={Kind} on_complete={Mode}",
            tenantId, config.Name, progress.ContactKey, completionNo, streakConfig.Reward.Kind, streakConfig.OnComplete);

        return earned;
    }
}
