using System.Data.Common;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.Ledger;

public class PointsExpirationJob(LoyaltyDbContext db, ILogger<PointsExpirationJob> logger)
{
    public async Task<ExpireRunResult> RunAsync(CancellationToken ct = default)
    {
        var today    = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayStr = today.ToString("yyyy-MM-dd");

        logger.LogInformation("PointsExpiration: started for {Date}", today);

        // EF Core SqlQueryRaw maps onto record properties by column name —
        // bind snake_case DB columns to PascalCase properties via aliases.
        var accountTypes = await db.Database
            .SqlQueryRaw<AccountTypeExpiry>("""
                SELECT
                    id                                       AS "Id",
                    tenant_id                                AS "TenantId",
                    name                                     AS "Name",
                    (config->>'expiration_days')::int        AS "ExpirationDays"
                FROM account_types
                WHERE type = 'POINTS'
                  AND (config->>'expiration_days') IS NOT NULL
                """)
            .ToListAsync(ct);

        if (accountTypes.Count == 0)
        {
            logger.LogInformation("PointsExpiration: no expirable account types, skipping");
            return new ExpireRunResult(0, 0);
        }

        var totalCustomers     = 0;
        var totalExpiredPoints = 0m;

        foreach (var at in accountTypes)
        {
            ct.ThrowIfCancellationRequested();

            // Fixed cutoff: today's UTC midnight - N days.
            // Produces the same cutoff whether the job runs at 00:00 or 00:59.
            var cutoffDate = DateTime.UtcNow.Date.AddDays(-at.ExpirationDays);

            var (customers, expired) = await ExpireForAccountTypeAsync(
                at.Id, at.Name, at.ExpirationDays, cutoffDate, todayStr, ct);

            logger.LogInformation(
                "PointsExpiration: account_type={AccountTypeId} tenant={Tenant} expiration_days={Days} " +
                "→ {Customers} customers, {Points} pts expired",
                at.Id, at.TenantId, at.ExpirationDays, customers, expired);

            totalCustomers     += customers;
            totalExpiredPoints += expired;
        }

        logger.LogInformation(
            "PointsExpiration: completed — {Customers} customers affected, {TotalPoints} pts expired",
            totalCustomers, totalExpiredPoints);

        return new ExpireRunResult(totalCustomers, totalExpiredPoints);
    }

    // For a single account type: FIFO computation + ledger INSERT + balance UPDATE +
    // summary are done in one SQL statement, in one transaction.
    private async Task<(int customers, decimal expired)> ExpireForAccountTypeAsync(
        Guid accountTypeId,
        string accountTypeName,
        int expirationDays,
        DateTime cutoffDate,
        string todayStr,
        CancellationToken ct)
    {
        // Critical idempotency: the INSERT and the balance UPDATE must be in one CTE chain.
        // If two separate SQLs were used (INSERT, then UPDATE FROM ledger_entries),
        // on the 2nd run NOT EXISTS blocks the INSERT but the UPDATE would still add the
        // delta of the existing entry to the balance → customer balance corrupted forever.
        //
        // The summary also comes from the same statement's final SELECT — only the rows
        // inserted in THIS run are counted. Had a separate summary query been used, a 2nd
        // run on the same day would report the first run's results as its own.
        //
        // FIFO logic (window function):
        //   For each earn entry, cumulative_before = SUM(delta) of prior earns
        //   An entry's consumed part = GREATEST(0, total_consumed - cumulative_before)
        //   Its remaining (expirable) part = GREATEST(0, delta - consumed)
        //   Only the remainder of entries with created_at <= cutoff is an expiry candidate.
        //
        // total_consumed = |SUM(delta)| of consumption reasons across all dates:
        //   points_redeemed / points_redeemed_cash / refund / points_expired / reward_purchase / transfer_out.
        const string sql = """
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
                JOIN tenants ca_tn ON ca_tn.id = ca.tenant_id
                WHERE ca.account_type_id = @account_type_id
                  AND ca.balance > 0
                  AND NOT EXISTS (
                      -- ledger_entries.tenant_id is the slug (that table stays exempt from the
                      -- tenant_id -> uuid migration), so the comparison goes through ca_tn.slug.
                      SELECT 1 FROM ledger_entries ex
                      WHERE ex.tenant_id       = ca_tn.slug
                        AND ex.idempotency_key = 'expire:' || ca.id::text || ':' || @today
                  )
            ),
            earn_entries AS (
                SELECT
                    le.customer_account_id AS account_id,
                    le.delta,
                    le.created_at,
                    SUM(le.delta) OVER (
                        PARTITION BY le.customer_account_id
                        ORDER BY le.created_at, le.id
                        ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
                    ) AS cumulative_before
                FROM ledger_entries le
                WHERE le.reason IN ('earn', 'transfer_in')
                  AND le.customer_account_id IN (SELECT account_id FROM consumption)
            ),
            expirable_per_entry AS (
                SELECT
                    e.account_id,
                    GREATEST(
                        0,
                        e.delta - GREATEST(
                            0,
                            c.total_consumed - COALESCE(e.cumulative_before, 0)
                        )
                    ) AS remaining
                FROM earn_entries e
                JOIN consumption c ON c.account_id = e.account_id
                WHERE e.created_at <= @cutoff    -- only earns before the cutoff
            ),
            to_expire AS (
                -- ledger_entries.tenant_id stays the slug (that table is deliberately exempt
                -- from the tenant_id -> uuid migration — see Tenant.cs's remarks), so the slug
                -- is looked up here via customer_accounts.tenant_id (uuid) -> tenants.slug.
                SELECT
                    tn.slug          AS tenant_slug,
                    ca.id            AS account_id,
                    ca.contact_key,
                    ca.account_type_id,
                    LEAST(et.expirable, ca.balance) AS amount
                FROM customer_accounts ca
                JOIN tenants tn ON tn.id = ca.tenant_id
                JOIN (
                    SELECT account_id, SUM(remaining) AS expirable
                    FROM expirable_per_entry
                    GROUP BY account_id
                ) et ON et.account_id = ca.id
                WHERE et.expirable > 0
                  AND ca.balance    > 0
            ),
            inserted AS (
                INSERT INTO ledger_entries (
                    id, tenant_id, customer_account_id, contact_key,
                    delta, reason, source_event_id, idempotency_key, metadata, created_at
                )
                SELECT
                    gen_random_uuid(),
                    te.tenant_slug, te.account_id, te.contact_key,
                    -te.amount,
                    'points_expired',
                    'expire:' || te.account_id::text || ':' || @today,
                    'expire:' || te.account_id::text || ':' || @today,
                    jsonb_build_object(
                        'account_type_id', te.account_type_id::text,
                        'expiration_days', @expiration_days,
                        'cutoff_date',     to_char(@cutoff AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
                    ),
                    NOW()
                FROM to_expire te
                WHERE te.amount > 0
                RETURNING tenant_id, customer_account_id, contact_key, delta
            ),
            updated AS (
                UPDATE customer_accounts ca
                SET
                    balance    = ca.balance + i.delta,
                    updated_at = NOW()
                FROM inserted i
                WHERE ca.id = i.customer_account_id
                RETURNING ca.id, ca.balance
            ),
            outboxed AS (
                INSERT INTO outbox_events (
                    event_id, tenant_id, event_type, contact_key,
                    payload, dedup_key, status, attempts, next_attempt_at, created_at
                )
                SELECT
                    x.event_id,
                    x.tenant_id,
                    'loyalty.points.expired',
                    x.contact_key,
                    jsonb_build_object(
                        'eventId',    x.event_id::text,
                        'eventType',  'loyalty.points.expired',
                        'tenant',     x.tenant_slug,
                        'occurredAt', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
                        'version',    '1',
                        'data', jsonb_build_object(
                            'contact_key',  x.contact_key,
                            'amount',       x.amount,
                            'account_type', 'POINTS',
                            'code',         x.code,
                            'balance',      x.balance,
                            'cutoff_date',  x.cutoff_str
                        )
                    ),
                    x.dedup_key,
                    'pending', 0, NOW(), NOW()
                FROM (
                    SELECT
                        gen_random_uuid()                        AS event_id,
                        ca.tenant_id,
                        tn.slug                                  AS tenant_slug,
                        i.contact_key,
                        to_char(ABS(i.delta), 'FM999999990.00')  AS amount,
                        to_char(u.balance,    'FM999999990.00')  AS balance,
                        @account_type_name                       AS code,
                        to_char(@cutoff, 'YYYY-MM-DD')           AS cutoff_str,
                        'points_expired:' || i.customer_account_id::text || ':' || @today AS dedup_key
                    FROM inserted i
                    JOIN updated u ON u.id = i.customer_account_id
                    JOIN customer_accounts ca ON ca.id = i.customer_account_id
                    JOIN tenants tn ON tn.id = ca.tenant_id
                ) x
                ON CONFLICT (tenant_id, dedup_key) WHERE dedup_key IS NOT NULL DO NOTHING
            )
            SELECT
                COUNT(*)::bigint                     AS customers,
                COALESCE(ABS(SUM(i.delta)), 0)       AS expired
            FROM inserted i
            """;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var conn = db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx.GetDbTransaction();
            cmd.CommandText = sql;
            AddParam(cmd, "account_type_id",   accountTypeId);
            AddParam(cmd, "account_type_name", accountTypeName);
            AddParam(cmd, "expiration_days",   expirationDays);
            AddParam(cmd, "today",             todayStr);
            AddParam(cmd, "cutoff",            cutoffDate);

            long customers;
            decimal expired;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                await reader.ReadAsync(ct);
                customers = reader.GetInt64(0);
                expired   = reader.GetDecimal(1);
            }

            await tx.CommitAsync(ct);
            return ((int)customers, expired);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}

// EF Core SqlQueryRaw throws IndexOutOfRangeException during reflection for
// file-local records — use an internal record.
internal record AccountTypeExpiry(Guid Id, Guid TenantId, string Name, int ExpirationDays);

public record ExpireRunResult(int CustomersAffected, decimal TotalPointsExpired);
