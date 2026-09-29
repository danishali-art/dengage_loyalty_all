namespace dEngage.Loyalty.RuleEngine.Processing;

// CR-07 (docs/scope-change-rules A7): the 6 new limit types beyond PerCustomerTotal/
// PerCustomerPerDay (which keep using ILimitCacheService's Redis-backed counters unchanged).
// Implemented as direct ledger queries — the same "rebuild from ledger history" pattern
// ILimitCacheService already falls back to on a cache miss — rather than a new cached/
// transactional counters store. CR-09 is where these get transactional/performance hardening
// (a real counters table, incremented in the same DB transaction as the posting); this is the
// correctness-first version that defines the limit types and enforces them.
public interface IRuleLimitEvaluator
{
    Task<bool> IsCooldownActiveAsync(string tenantId, Guid ruleId, string contactKey, decimal cooldownHours, CancellationToken ct);

    // True only when contactKey has never benefited from this rule before AND the distinct
    // customer count has already reached maxCustomers — an existing customer is never blocked.
    Task<bool> IsNewCustomerBlockedByMaxAsync(string tenantId, Guid ruleId, string contactKey, int maxCustomers, CancellationToken ct);

    // Sum of |delta| this rule has posted, across all customers, optionally period-scoped.
    Task<decimal> GetRuleBudgetUsedAsync(string tenantId, Guid ruleId, string? period, string? resetWindow, CancellationToken ct);

    // Sum of this rule's Earn/StampEarn/Refund postings for one customer, period-scoped —
    // parallel to ILimitCacheService.GetDailyAsync but with a configurable period.
    Task<decimal> GetCustomerPeriodUsedAsync(string tenantId, Guid ruleId, string contactKey, string? period, string? resetWindow, CancellationToken ct);
}
