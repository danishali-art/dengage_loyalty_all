using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine;

public class TierDowngradeJob(LoyaltyDbContext db, ILogger<TierDowngradeJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        logger.LogInformation("TierDowngrade: started for {Date}", today);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var graceStarted = await StartGraceAsync(today, ct);
            var downgraded   = await DowngradeExpiredAsync(today, ct);

            await tx.CommitAsync(ct);

            logger.LogInformation("TierDowngrade: completed — grace={Grace} downgraded={Downgraded}",
                graceStarted, downgraded);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Step 1: Start grace ─────────────────────────────────────────────
    // period_end = tier_period_start + qualifying_days
    // If period_end <= today and tier_expires_at IS NULL → set it.
    // DateOnly → timestamptz cast is done as UTC (independent of session TZ).
    private async Task<int> StartGraceAsync(DateOnly today, CancellationToken ct)
    {
        var graceStarted = await db.Database.ExecuteSqlRawAsync("""
            UPDATE customer_accounts ca
            SET
                tier_expires_at = (ca.tier_period_start + (td.qualifying_days || ' days')::interval)::date
                                  + (td.grace_days || ' days')::interval,
                updated_at = NOW()
            FROM tier_definitions td
            JOIN account_types qat ON qat.program_id = td.program_id AND qat.is_tier_qualifying
            WHERE ca.tier_id           = td.id
              AND ca.account_type_id   = qat.id
              AND td.qualifying_days   IS NOT NULL
              AND ca.tier_period_start IS NOT NULL
              AND ca.tier_expires_at   IS NULL
              AND (ca.tier_period_start + (td.qualifying_days || ' days')::interval)::date <= {0}
              -- CR 2026-09-30 (§3.6): a reward-driven upgrade is protected until tier_locked_until;
              -- grace only starts once the lock has passed.
              AND (ca.tier_locked_until IS NULL OR ca.tier_locked_until <= {0})
            """, today);

        if (graceStarted > 0)
            logger.LogInformation("TierDowngrade: grace initiated for {Count} accounts", graceStarted);

        return graceStarted;
    }

    // ── Step 2: Re-evaluate accounts whose period has ended ──────────
    // First we write which account moves to which new tier into the pending_downgrades
    // table (from_tier_id + new_tier_id). Then we run UPDATE + LOG as two separate SQLs.
    // This way we do not lose the real "from" info during the log INSERT.
    //
    // The qualifying window is by design [period_start, period_start + qualifying_days) —
    // points earned during grace DO NOT count toward requalification (tier_design.md).
    private async Task<int> DowngradeExpiredAsync(DateOnly today, CancellationToken ct)
    {
        // 2a. Compute pending downgrades (temp table)
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TEMP TABLE pending_downgrades ON COMMIT DROP AS
            WITH
            program_tiers AS (
                SELECT
                    td.id, td.program_id, td.min_points, td.sort_order,
                    ROW_NUMBER() OVER (PARTITION BY td.program_id ORDER BY td.sort_order) AS rn
                FROM tier_definitions td
            ),
            expired_accounts AS (
                SELECT
                    ca.id            AS account_id,
                    ca.tenant_id,
                    ca.contact_key,
                    ca.tier_id       AS old_tier_id,
                    p.id             AS program_id,
                    COALESCE((
                        SELECT SUM(le.delta)
                        FROM ledger_entries le
                        WHERE le.customer_account_id = ca.id
                          AND le.reason IN ('earn', 'stamp_earn')
                          AND le.created_at >= (ca.tier_period_start::timestamp AT TIME ZONE 'UTC')
                          AND le.created_at <  ((ca.tier_period_start + (td.qualifying_days || ' days')::interval) AT TIME ZONE 'UTC')
                    ), 0) AS period_qualifying_pts
                FROM customer_accounts ca
                JOIN tier_definitions td ON td.id = ca.tier_id
                JOIN programs p ON p.id = td.program_id
                -- 1.3.CL item 1: the qualifying wallet is AccountType.IsTierQualifying now.
                JOIN account_types qat ON qat.id = ca.account_type_id
                                      AND qat.program_id = td.program_id
                                      AND qat.is_tier_qualifying
                WHERE ca.tier_id           IS NOT NULL
                  AND ca.tier_expires_at   IS NOT NULL
                  AND ca.tier_period_start IS NOT NULL
                  AND td.qualifying_days   IS NOT NULL
                  AND ca.tier_expires_at <= {0}
                  -- CR 2026-09-30 (§3.6): never downgrade a tier still locked by a reward.
                  AND (ca.tier_locked_until IS NULL OR ca.tier_locked_until <= {0})
            ),
            best_tier AS (
                SELECT DISTINCT ON (ea.account_id)
                    ea.account_id, ea.tenant_id, ea.contact_key, ea.old_tier_id,
                    ea.period_qualifying_pts, pt.id AS new_tier_id
                FROM expired_accounts ea
                JOIN program_tiers pt
                  ON pt.program_id = ea.program_id
                 AND pt.min_points <= ea.period_qualifying_pts
                ORDER BY ea.account_id, pt.sort_order DESC
            ),
            lowest_tier AS (
                SELECT DISTINCT ON (ea.account_id)
                    ea.account_id, ea.tenant_id, ea.contact_key, ea.old_tier_id,
                    ea.period_qualifying_pts, pt.id AS new_tier_id
                FROM expired_accounts ea
                JOIN program_tiers pt ON pt.program_id = ea.program_id
                WHERE NOT EXISTS (SELECT 1 FROM best_tier bt WHERE bt.account_id = ea.account_id)
                ORDER BY ea.account_id, pt.rn
            )
            SELECT * FROM best_tier
            UNION ALL
            SELECT * FROM lowest_tier
            """, today);

        // 2b. UPDATE — assign the new tier, start a new period.
        // CAUTION: the period must be reset for ALL accounts whose period ended, including
        // those requalifying for the same tier — otherwise tier_expires_at stays set and
        // the account gets re-evaluated every night, the period never advances. The
        // tier-change filter is applied only in the log INSERT (2c).
        var downgraded = await db.Database.ExecuteSqlRawAsync("""
            UPDATE customer_accounts ca
            SET
                tier_id             = pd.new_tier_id,
                tier_qualifying_pts = 0,
                tier_period_start   = {0},
                tier_expires_at     = NULL,
                updated_at          = NOW()
            FROM pending_downgrades pd
            WHERE ca.id = pd.account_id
            """, today);

        // 2c. LOG — take from_tier_id from the temp table, idempotency via source_event_id
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO tier_upgrade_log (
                id, tenant_id, contact_key,
                from_tier_id, to_tier_id,
                qualifying_pts, source_event_id, created_at
            )
            SELECT
                gen_random_uuid(),
                pd.tenant_id,
                pd.contact_key,
                pd.old_tier_id,
                pd.new_tier_id,
                pd.period_qualifying_pts,
                'downgrade:' || pd.account_id::text || ':' || {0},
                NOW()
            FROM pending_downgrades pd
            WHERE pd.old_tier_id IS DISTINCT FROM pd.new_tier_id
              AND NOT EXISTS (
                  SELECT 1 FROM tier_upgrade_log tul
                  WHERE tul.tenant_id       = pd.tenant_id
                    AND tul.contact_key     = pd.contact_key
                    AND tul.source_event_id = 'downgrade:' || pd.account_id::text || ':' || {0}
              )
            """, today);

        // 2d. OUTBOX — tier.changed (down) in the same tx. eventId is generated once in
        // the subquery (column + payload see the same value). If the job reruns the same
        // day, it lands on the dedup_key partial unique index via ON CONFLICT DO NOTHING.
        // Re-evaluation could in theory also move upward → direction via CASE.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO outbox_events (
                event_id, tenant_id, event_type, contact_key,
                payload, dedup_key, status, attempts, next_attempt_at, created_at
            )
            SELECT
                x.event_id,
                x.tenant_id,
                'loyalty.tier.changed',
                x.contact_key,
                jsonb_build_object(
                    'eventId',    x.event_id::text,
                    'eventType',  'loyalty.tier.changed',
                    'tenant',     x.tenant_id,
                    'occurredAt', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
                    'version',    '1',
                    'data', jsonb_build_object(
                        'contact_key',       x.contact_key,
                        'from_tier',         x.from_name,
                        'to_tier',           x.to_name,
                        'direction',         x.direction,
                        'qualifying_points', x.qualifying_points
                    )
                ),
                x.dedup_key,
                {1}, 0, NOW(), NOW()
            FROM (
                SELECT
                    gen_random_uuid() AS event_id,
                    pd.tenant_id,
                    pd.contact_key,
                    tf.name AS from_name,
                    tt.name AS to_name,
                    CASE WHEN tt.sort_order < tf.sort_order THEN {2} ELSE {3} END AS direction,
                    to_char(pd.period_qualifying_pts, 'FM999999990.00') AS qualifying_points,
                    'tier_changed:downgrade:' || pd.account_id::text || ':' || {0} AS dedup_key
                FROM pending_downgrades pd
                JOIN tier_definitions tf ON tf.id = pd.old_tier_id
                JOIN tier_definitions tt ON tt.id = pd.new_tier_id
                WHERE pd.old_tier_id IS DISTINCT FROM pd.new_tier_id
            ) x
            ON CONFLICT (tenant_id, dedup_key) WHERE dedup_key IS NOT NULL DO NOTHING
            """, today, OutboxStatus.Pending, TierChangeDirection.Down, TierChangeDirection.Up);

        if (downgraded > 0)
            logger.LogInformation("TierDowngrade: period rolled for {Count} accounts (tier changes logged separately)", downgraded);

        return downgraded;
    }
}
