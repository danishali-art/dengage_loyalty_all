using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;

namespace dEngage.Loyalty.RuleEngine.Metadata;

// CR 2026-10-06 Phase 2 (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md §3.2):
// which Configuration and Limits fields mean something for a rule of a given type on a given
// trigger — the others are ignored by the code that applies the rule, or harmful (e.g. Min event
// amount on an event without an amount never fires). The single source of truth, next to
// RuleTypeCatalog: the API checks new rules against it and `GET rules/metadata` serves it to the
// portal's rule form, so the list is never duplicated. Applied to NEW rules only (D5) — existing
// rules keep their saved values and are edited as before.
public static class RuleFieldCatalog
{
    // Configuration keys (RuleSettings' JSON names). testMode is gone (D20).
    public const string Rounding = "rounding";
    public const string Posting = "posting";
    public const string HoldDays = "holdDays";
    public const string ExpiryOverrideDays = "expiryOverrideDays";
    public const string Reversible = "reversible";
    public const string NotifyOnAward = "notifyOnAward";

    // Limit keys (RuleLimits' JSON names).
    public const string PerCustomerTotal = "per_customer_total";
    public const string PerCustomerPerDay = "per_customer_per_day";
    public const string PerCustomerPerPeriod = "per_customer_per_period";
    public const string MaxPerEvent = "max_per_event";
    public const string MinEventAmount = "min_event_amount";
    public const string CooldownHours = "cooldown_hours";
    public const string MaxCustomers = "max_customers";
    public const string RuleBudgetTotal = "rule_budget_total";
    public const string RuleBudgetPerPeriod = "rule_budget_per_period";
    public const string Period = "period";
    public const string ResetWindow = "reset_window";
    public const string OnBreach = "on_breach";

    public static readonly IReadOnlyList<string> AllConfiguration =
        [Rounding, Posting, HoldDays, ExpiryOverrideDays, Reversible, NotifyOnAward];

    public static readonly IReadOnlyList<string> AllLimits =
    [
        PerCustomerTotal, PerCustomerPerDay, PerCustomerPerPeriod, MaxPerEvent, MinEventAmount,
        CooldownHours, MaxCustomers, RuleBudgetTotal, RuleBudgetPerPeriod, Period, ResetWindow, OnBreach
    ];

    public sealed record ApplicableFields(IReadOnlyList<string> Configuration, IReadOnlyList<string> Limits);

    private static readonly ApplicableFields Nothing = new([], []);

    // evt null = a tenant-defined trigger: no field metadata, so every field stays (D7; the
    // portal adds a hint where an amount is needed, D14).
    public static ApplicableFields For(EventDefinition? evt, string ruleType)
    {
        // The Reversal processor reads neither section (§2.2, §2.5).
        if (ruleType == RuleTypes.ReversalRule) return Nothing;
        if (evt is null) return new(AllConfiguration, AllLimits);

        return ruleType switch
        {
            // Burn rules: their handler applies conditions, min event amount, cooldown, max
            // customers and budgets; their minimum lives in the terms (D11), On breach doesn't
            // apply (a burn budget is all-or-nothing), the rest is ignored.
            RuleTypes.RedemptionRule or RuleTypes.TransferRule =>
                new([], [CooldownHours, MaxCustomers, RuleBudgetTotal, RuleBudgetPerPeriod, Period, ResetWindow]),

            // Operator adjustments post now and carry no limits (§3.2 group E).
            RuleTypes.ManualAdjustmentRule => new([Rounding], []),

            // signup / kyc.completed: once per customer per rule (D4b hides Per customer total),
            // no amount (Min event amount would never fire), nothing refunds them (no
            // Reversible), no delayed posting (D10).
            _ when evt.Cardinality == EventCardinality.OncePerCustomer =>
                new([Rounding, ExpiryOverrideDays, NotifyOnAward],
                    [MaxCustomers, RuleBudgetTotal, RuleBudgetPerPeriod, Period, ResetWindow, OnBreach]),

            // Amount-based earn: everything, except Max per event on a fixed bonus (the amount is
            // already fixed).
            RuleTypes.FixedBonusRule => new(AllConfiguration, AllLimits.Where(l => l != MaxPerEvent).ToList()),
            _ => new(AllConfiguration, AllLimits)
        };
    }

    // A non-default value in a field that doesn't apply — what a NEW rule may not carry. A field
    // left at its default (Immediate posting, reversible, no limit, On breach Clamp) is fine, so a
    // client that always sends the defaults keeps working.
    public static IReadOnlyList<string> SetButNotApplicable(ApplicableFields applicable, RuleSettings? configuration, RuleLimits? limits)
    {
        var set = new List<string>();
        if (configuration is not null)
        {
            if (configuration.Rounding is not null) set.Add(Rounding);
            if (configuration.Posting is not null and not "Immediate") set.Add(Posting);
            if (configuration.HoldDays is not null) set.Add(HoldDays);
            if (configuration.ExpiryOverrideDays is not null) set.Add(ExpiryOverrideDays);
            if (!configuration.Reversible) set.Add(Reversible);
            if (configuration.NotifyOnAward) set.Add(NotifyOnAward);
        }
        if (limits is not null)
        {
            if (limits.PerCustomerTotal is not null) set.Add(PerCustomerTotal);
            if (limits.PerCustomerPerDay is not null) set.Add(PerCustomerPerDay);
            if (limits.PerCustomerPerPeriod is not null) set.Add(PerCustomerPerPeriod);
            if (limits.MaxPerEvent is not null) set.Add(MaxPerEvent);
            if (limits.MinEventAmount is not null) set.Add(MinEventAmount);
            if (limits.CooldownHours is not null) set.Add(CooldownHours);
            if (limits.MaxCustomers is not null) set.Add(MaxCustomers);
            if (limits.RuleBudgetTotal is not null) set.Add(RuleBudgetTotal);
            if (limits.RuleBudgetPerPeriod is not null) set.Add(RuleBudgetPerPeriod);
            if (limits.Period is not null) set.Add(Period);
            if (limits.ResetWindow is not null) set.Add(ResetWindow);
            if (limits.OnBreach is not null and not "Clamp") set.Add(OnBreach);
        }

        return set.Where(f => !applicable.Configuration.Contains(f) && !applicable.Limits.Contains(f)).ToList();
    }
}
