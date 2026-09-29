namespace dEngage.Loyalty.RuleEngine.Processing;

// CR-09 (docs/scope-change-rules A10 guarantee #4): "reserve, post, confirm" for
// RuleBudgetTotal/RuleBudgetPerPeriod — closes the TOCTOU race CR-07's ledger-query check has
// (two concurrent events for the same rule could both read "budget remaining" before either
// commits). Must be called from WITHIN the same DB transaction/DbContext as the posting it's
// reserving for — the row lock only serializes concurrent callers sharing that transaction
// boundary, not calls made outside one.
public interface IBudgetReservationService
{
    // Locks the counter row for (ruleId, counterType, periodKey), creating and seeding it from
    // ledger history on first use, and returns the current usage. Blocks until any concurrent
    // transaction holding the same lock commits or rolls back. `period` ("Day"|"Week"|"Month"|
    // "Year", null for the lifetime/budget_total counter) is only used to compute the ledger
    // seed correctly — the caller has already folded it into periodKey via PeriodWindow.CalendarKey.
    Task<decimal> LockAndGetUsageAsync(string tenantId, Guid ruleId, string counterType, string periodKey, string? period, CancellationToken ct);

    // Adds delta to the locked counter — call after LockAndGetUsageAsync, in the same
    // transaction, once the final (possibly clamped) amount is known.
    Task RecordUsageAsync(string tenantId, Guid ruleId, string counterType, string periodKey, decimal delta, CancellationToken ct);

    // Guarantee #5: subtracts the released amount (floored at 0) from every counter row this
    // rule has (both budget_total and any budget_period buckets) — called when a reversal posts
    // against a rule that had budget reserved. Not locked itself (callers already hold/don't
    // need the lock for a release), but still runs inside the caller's transaction.
    Task ReleaseUsageAsync(string tenantId, Guid ruleId, decimal amount, CancellationToken ct);
}
