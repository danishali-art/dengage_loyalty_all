namespace dEngage.Loyalty.Schema.Entities;

// CR-09 (docs/scope-change-rules A10 guarantee #4): durable, lockable counters for
// RuleBudgetTotal/RuleBudgetPerPeriod — the two limit types A7 literally calls "budget." Scoped
// to just these two (not every CR-07 limit type) because guarantee #4 specifically names
// "budget reservation," and locking every limit type would be a much bigger change for less
// value — cardinality/cooldown/max-customers stay on CR-07's ledger-query approach.
// PeriodKey is "" (not null) for the lifetime/total counter — Postgres unique constraints treat
// NULL as distinct from NULL, which would silently break the uniqueness this table depends on.
public class RuleLimitCounter
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RuleId { get; set; }
    public string CounterType { get; set; } = default!; // "budget_total" | "budget_period"
    public string PeriodKey { get; set; } = "";
    public decimal Value { get; set; }
    public DateTime UpdatedAt { get; set; }
}
