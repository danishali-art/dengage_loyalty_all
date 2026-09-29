using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Ledger;

public class EventLogRetentionJob(LoyaltyDbContext db, ILogger<EventLogRetentionJob> logger)
{
    // occurred_within windows are capped at 8760h (ConditionDsl.MaxWindowHours),
    // so nothing older than 12 months can ever satisfy a rule.
    private const int BatchSize = 10_000;

    public async Task<long> RunAsync(CancellationToken ct = default)
    {
        logger.LogInformation("EventLogRetention: started");

        long total = 0;
        while (!ct.IsCancellationRequested)
        {
            var deleted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM event_log
                WHERE (tenant_id, event_id) IN (
                    SELECT tenant_id, event_id FROM event_log
                    WHERE occurred_at < now() - interval '12 months'
                    LIMIT {BatchSize}
                )
                """, ct);

            total += deleted;
            if (deleted < BatchSize) break;
        }

        logger.LogInformation("EventLogRetention: completed — {Total} rows deleted", total);
        return total;
    }
}
