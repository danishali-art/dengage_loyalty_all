using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Shared;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.RuleEngine.Processing;

public sealed class RuleLimitEvaluator(LoyaltyDbContext db) : IRuleLimitEvaluator
{
    private static readonly string[] EarnReasons = { LedgerReason.Earn, LedgerReason.StampEarn, LedgerReason.Refund };

    public Task<bool> HasOnceOnlyAwardAsync(string tenantId, Guid ruleId, string contactKey, CancellationToken ct) =>
        OnceOnlyAward.ExistsAsync(db, tenantId, ruleId, contactKey, ct);

    public async Task<bool> IsCooldownActiveAsync(string tenantId, Guid ruleId, string contactKey, decimal cooldownHours, CancellationToken ct)
    {
        if (cooldownHours <= 0) return false;

        var lastPostedAt = await db.LedgerEntries
            .Where(x => x.TenantId == tenantId && x.RuleId == ruleId && x.ContactKey == contactKey)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => (DateTime?)x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (lastPostedAt is null) return false;
        return (DateTime.UtcNow - lastPostedAt.Value) < TimeSpan.FromHours((double)cooldownHours);
    }

    public async Task<bool> IsNewCustomerBlockedByMaxAsync(string tenantId, Guid ruleId, string contactKey, int maxCustomers, CancellationToken ct)
    {
        if (maxCustomers <= 0) return false;

        var alreadyBenefited = await db.LedgerEntries
            .AnyAsync(x => x.TenantId == tenantId && x.RuleId == ruleId && x.ContactKey == contactKey, ct);
        if (alreadyBenefited) return false; // existing customers are never blocked

        var distinctCustomers = await db.LedgerEntries
            .Where(x => x.TenantId == tenantId && x.RuleId == ruleId)
            .Select(x => x.ContactKey)
            .Distinct()
            .CountAsync(ct);

        return distinctCustomers >= maxCustomers;
    }

    // "Total value this rule has moved" — abs(delta) so the cap applies uniformly whether the
    // rule earns (positive deltas) or burns/adjusts (negative or signed deltas); not restricted
    // to the Earn-shaped reason set, since a budget cap is a reasonable control on any rule type.
    public async Task<decimal> GetRuleBudgetUsedAsync(string tenantId, Guid ruleId, string? period, string? resetWindow, CancellationToken ct)
    {
        var query = db.LedgerEntries.Where(x => x.TenantId == tenantId && x.RuleId == ruleId);
        if (period is not null)
        {
            var start = PeriodWindow.Start(period, resetWindow, DateTime.UtcNow);
            query = query.Where(x => x.CreatedAt >= start);
        }
        // Math.Abs(decimal) inside Sum() doesn't translate on every provider (confirmed against
        // Sqlite, used by the test harness) — pull the deltas and sum client-side instead.
        var deltas = await query.Select(x => x.Delta).ToListAsync(ct);
        return deltas.Sum(Math.Abs);
    }

    public async Task<decimal> GetCustomerPeriodUsedAsync(string tenantId, Guid ruleId, string contactKey, string? period, string? resetWindow, CancellationToken ct)
    {
        var start = PeriodWindow.Start(period, resetWindow, DateTime.UtcNow);
        return await db.LedgerEntries
            .Where(x => x.TenantId == tenantId && x.RuleId == ruleId && x.ContactKey == contactKey &&
                        EarnReasons.Contains(x.Reason) && x.CreatedAt >= start)
            .SumAsync(x => (decimal?)x.Delta, ct) ?? 0m;
    }
}
