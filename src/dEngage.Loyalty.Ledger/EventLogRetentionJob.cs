using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Ledger;

public class EventLogRetentionJob(LoyaltyDbContext db, ILoyaltyConfig loyaltyConfig, ILogger<EventLogRetentionJob> logger)
{
    // occurred_within windows are capped at 8760h (ConditionDsl.MaxWindowHours),
    // so nothing older than 12 months can ever satisfy a rule.

    public async Task<long> RunAsync(CancellationToken ct = default)
    {
        logger.LogInformation("EventLogRetention: started");
        // From loyalty_config (alignment D4).
        var batchSize = await loyaltyConfig.GetAsync<int>(
            LoyaltyConfigKeys.Jobs.Module, LoyaltyConfigKeys.Jobs.EventLogRetentionBatchSize, ct);
        if (batchSize <= 0)
            throw new InvalidOperationException("loyalty_config_invalid: loyalty.jobs/EventLogRetention.BatchSize must be positive.");

        long total = 0;
        while (!ct.IsCancellationRequested)
        {
            var deleted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM event_log
                WHERE (tenant_id, event_id) IN (
                    SELECT tenant_id, event_id FROM event_log
                    WHERE occurred_at < now() - interval '12 months'
                    LIMIT {batchSize}
                )
                """, ct);

            total += deleted;
            if (deleted < batchSize) break;
        }

        logger.LogInformation("EventLogRetention: completed — {Total} rows deleted", total);
        return total;
    }
}
