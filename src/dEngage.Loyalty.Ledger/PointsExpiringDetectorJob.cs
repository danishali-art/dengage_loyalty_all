using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Ledger;

public class PointsExpiringDetectorJob(LoyaltyDbContext db, ILogger<PointsExpiringDetectorJob> logger)
{
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        logger.LogInformation("PointsExpiringDetector: started");

        // 1.3.CL item 1: warning_days is per POINTS wallet (config key), no longer programs.warning_days.
        var accountTypes = await db.Database
            .SqlQueryRaw<AccountTypeWarning>("""
                SELECT
                    at.id                                    AS "Id",
                    at.tenant_id                             AS "TenantId",
                    at.name                                  AS "Name",
                    (at.config->>'expiration_days')::int     AS "ExpirationDays",
                    (at.config->>'warning_days')::int        AS "WarningDays"
                FROM account_types at
                WHERE at.type = 'POINTS'
                  AND (at.config->>'expiration_days') IS NOT NULL
                  AND (at.config->>'warning_days') IS NOT NULL
                """)
            .ToListAsync(ct);

        if (accountTypes.Count == 0)
        {
            logger.LogInformation("PointsExpiringDetector: no account types with warning_days, skipping");
            return 0;
        }

        var totalWarnings = 0;

        foreach (var at in accountTypes)
        {
            ct.ThrowIfCancellationRequested();

            if (at.WarningDays <= 0 || at.WarningDays >= at.ExpirationDays)
            {
                logger.LogWarning(
                    "PointsExpiringDetector: account_type={AccountTypeId} tenant={Tenant} " +
                    "warning_days={WarningDays} invalid (must satisfy 0 < warning_days < expiration_days={ExpirationDays}), skipping",
                    at.Id, at.TenantId, at.WarningDays, at.ExpirationDays);
                continue;
            }

            // "will expire within warning_days days": lots whose effective expiry date (CR
            // 2026-10-06 Phase 5) is on or before today + warning_days — for a lot without its own
            // date exactly the old "earned before today - (expiration_days - warning_days)".
            var warnBefore = DateTime.UtcNow.Date.AddDays(at.WarningDays);

            var warnings = await db.Database.ExecuteSqlRawAsync(
                DetectSql, [at.Id, at.Name, at.ExpirationDays, warnBefore], ct);

            logger.LogInformation(
                "PointsExpiringDetector: account_type={AccountTypeId} tenant={Tenant} " +
                "warning_days={WarningDays} → {Warnings} warnings generated",
                at.Id, at.TenantId, at.WarningDays, warnings);

            totalWarnings += warnings;
        }

        logger.LogInformation("PointsExpiringDetector: completed — {Total} warnings", totalWarnings);
        return totalWarnings;
    }

    // Same as PointsExpirationJob's allocation CTE (soonest-expiring first, CR 2026-10-06 Phase 5),
    // with two differences:
    //   1. lots expiring on or before {3} = today + warning_days
    //   2. Does NOT touch the ledger/balance — only writes a warning row to the outbox.
    //
    // Idempotency is in the outbox, not the ledger: dedup_key = expiring:{account}:{expires_on}
    // → a single warning per window (same first expiry date). When the first wave expires/gets
    // consumed and the next earn's date moves to the front, new window = new warning.
    //
    // expires_on = the earliest effective expiry date among lots with a remainder (the first
    // day anything expires). amount is the remainder of ALL lots expiring by {3} (capped by
    // balance). A lot dated by an expiry override is warned about too (in a wallet that has
    // warning_days — a wallet without expiration_days has no warning_days).
    //
    // Parameters: {0}=account_type_id, {1}=account_type_name,
    //             {2}=expiration_days, {3}=warn_before (UTC)
    private const string DetectSql = """
        WITH consumption AS (
            SELECT
                ca.id AS account_id,
                ABS(COALESCE((
                    SELECT SUM(delta)
                    FROM ledger_entries
                    WHERE customer_account_id = ca.id
                      AND reason IN (
                          'points_redeemed',
                          'points_redeemed_cash',
                          'refund',
                          'points_expired',
                          'reward_purchase',
                          'transfer_out'
                      )
                ), 0)) AS total_consumed
            FROM customer_accounts ca
            WHERE ca.account_type_id = {0}
              AND ca.balance > 0
        ),
        lots AS (
            SELECT
                le.customer_account_id AS account_id,
                le.id,
                le.delta,
                le.created_at,
                COALESCE(le.expires_at, le.created_at + make_interval(days => {2})) AS effective_expires_at
            FROM ledger_entries le
            WHERE le.reason IN ('earn', 'transfer_in')
              AND le.customer_account_id IN (SELECT account_id FROM consumption)
        ),
        earn_entries AS (
            SELECT
                l.account_id,
                l.delta,
                l.created_at,
                l.effective_expires_at,
                SUM(l.delta) OVER (
                    PARTITION BY l.account_id
                    ORDER BY l.effective_expires_at ASC NULLS LAST, l.created_at, l.id
                    ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
                ) AS cumulative_before
            FROM lots l
        ),
        expirable_per_entry AS (
            SELECT
                e.account_id,
                e.effective_expires_at,
                GREATEST(
                    0,
                    e.delta - GREATEST(
                        0,
                        c.total_consumed - COALESCE(e.cumulative_before, 0)
                    )
                ) AS remaining
            FROM earn_entries e
            JOIN consumption c ON c.account_id = e.account_id
            WHERE e.effective_expires_at <= {3}
        ),
        to_warn AS (
            SELECT
                ca.tenant_id,
                tn.slug          AS tenant_slug,
                ca.id            AS account_id,
                ca.contact_key,
                LEAST(et.expirable, ca.balance) AS amount,
                (et.first_expiring_at AT TIME ZONE 'UTC')::date AS expires_on
            FROM customer_accounts ca
            JOIN tenants tn ON tn.id = ca.tenant_id
            JOIN (
                SELECT
                    account_id,
                    SUM(remaining) AS expirable,
                    MIN(effective_expires_at) FILTER (WHERE remaining > 0) AS first_expiring_at
                FROM expirable_per_entry
                GROUP BY account_id
            ) et ON et.account_id = ca.id
            WHERE et.expirable > 0
              AND ca.balance    > 0
        )
        INSERT INTO outbox_events (
            event_id, tenant_id, event_type, contact_key,
            payload, dedup_key, status, attempts, next_attempt_at, created_at
        )
        SELECT
            x.event_id,
            x.tenant_id,
            'loyalty.points.expiring',
            x.contact_key,
            jsonb_build_object(
                'eventId',    x.event_id::text,
                'eventType',  'loyalty.points.expiring',
                'tenant',     x.tenant_slug,
                'occurredAt', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
                'version',    '1',
                'data', jsonb_build_object(
                    'contact_key',  x.contact_key,
                    'amount',       x.amount,
                    'account_type', 'POINTS',
                    'code',         x.code,
                    'expires_on',   x.expires_on
                )
            ),
            x.dedup_key,
            'pending', 0, NOW(), NOW()
        FROM (
            SELECT
                gen_random_uuid()                     AS event_id,
                tw.tenant_id,
                tw.tenant_slug,
                tw.contact_key,
                to_char(tw.amount, 'FM999999990.00')  AS amount,
                {1}                                   AS code,
                to_char(tw.expires_on, 'YYYY-MM-DD')  AS expires_on,
                'expiring:' || tw.account_id::text || ':' || to_char(tw.expires_on, 'YYYY-MM-DD') AS dedup_key
            FROM to_warn tw
            WHERE tw.amount > 0
        ) x
        ON CONFLICT (tenant_id, dedup_key) WHERE dedup_key IS NOT NULL DO NOTHING
        """;
}

// EF Core SqlQueryRaw throws IndexOutOfRangeException during reflection for
// file-local records — use an internal record.
internal record AccountTypeWarning(Guid Id, Guid TenantId, string Name, int ExpirationDays, int WarningDays);
