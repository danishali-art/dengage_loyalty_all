using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.RuleEngine.Processing;

public sealed class BudgetReservationService(
    LoyaltyDbContext db,
    ITenantSlugResolver tenantSlugResolver,
    IRuleLimitEvaluator limitEvaluator) : IBudgetReservationService
{
    public const string BudgetTotal = "budget_total";
    public const string BudgetPeriod = "budget_period";

    // Sqlite (used by the fast in-memory IntegrationTests harness) has no "FOR UPDATE" —
    // it locks at the database/transaction level by default, so plain reads inside a
    // transaction are already serialized against concurrent writers there. Production runs on
    // Postgres, where the explicit row lock is what actually closes the TOCTOU race — this
    // check is purely about which SQL dialect to emit, not a correctness difference for Sqlite.
    private bool IsPostgres => db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    public async Task<decimal> LockAndGetUsageAsync(string tenantId, Guid ruleId, string counterType, string periodKey, string? period, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        // Seed from ledger history on first use — matches what the CR-07 ledger-query check
        // would have reported, so a rule that already has postings before this transactional
        // path takes over doesn't start back at 0. ON CONFLICT DO NOTHING makes this a no-op
        // once the row exists.
        var seed = counterType == BudgetTotal
            ? await limitEvaluator.GetRuleBudgetUsedAsync(tenantId, ruleId, null, null, ct)
            : await limitEvaluator.GetRuleBudgetUsedAsync(tenantId, ruleId, period, "Calendar", ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO rule_limit_counters (id, tenant_id, rule_id, counter_type, period_key, value, updated_at)
            VALUES ({Guid.NewGuid()}, {tenantGuid}, {ruleId}, {counterType}, {periodKey}, {seed}, {DateTime.UtcNow})
            ON CONFLICT (tenant_id, rule_id, counter_type, period_key) DO NOTHING
            """, ct);

        // Locks the row — a concurrent transaction doing the same for this exact
        // (rule, counterType, periodKey) blocks here until this transaction commits/rolls back.
        // Two separate statements (rather than interpolating the FOR UPDATE fragment as a SQL
        // parameter, which EF would try to bind as a value, not raw syntax).
        if (IsPostgres)
        {
            return await db.Database.SqlQuery<decimal>($"""
                SELECT value AS "Value" FROM rule_limit_counters
                WHERE tenant_id = {tenantGuid} AND rule_id = {ruleId} AND counter_type = {counterType} AND period_key = {periodKey}
                FOR UPDATE
                """).FirstAsync(ct);
        }
        return await db.Database.SqlQuery<decimal>($"""
            SELECT value AS "Value" FROM rule_limit_counters
            WHERE tenant_id = {tenantGuid} AND rule_id = {ruleId} AND counter_type = {counterType} AND period_key = {periodKey}
            """).FirstAsync(ct);
    }

    public async Task RecordUsageAsync(string tenantId, Guid ruleId, string counterType, string periodKey, decimal delta, CancellationToken ct)
    {
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE rule_limit_counters
            SET value = value + {delta}, updated_at = {DateTime.UtcNow}
            WHERE tenant_id = {tenantGuid} AND rule_id = {ruleId} AND counter_type = {counterType} AND period_key = {periodKey}
            """, ct);
    }

    // Guarantee #5: floored at 0 — a release can never push a counter negative (e.g. a partial
    // reversal after other postings already consumed more budget).
    public async Task ReleaseUsageAsync(string tenantId, Guid ruleId, decimal amount, CancellationToken ct)
    {
        if (amount <= 0) return;
        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);
        // GREATEST (Postgres) vs MAX as a scalar 2-arg function (Sqlite) — same "floor at 0" clamp.
        if (IsPostgres)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE rule_limit_counters
                SET value = GREATEST(value - {amount}, 0), updated_at = {DateTime.UtcNow}
                WHERE tenant_id = {tenantGuid} AND rule_id = {ruleId}
                """, ct);
        }
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE rule_limit_counters
                SET value = MAX(value - {amount}, 0), updated_at = {DateTime.UtcNow}
                WHERE tenant_id = {tenantGuid} AND rule_id = {ruleId}
                """, ct);
        }
    }
}
