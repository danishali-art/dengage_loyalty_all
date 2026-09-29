using System.Text.Json;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Calculation;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Processing;

// CR-06 (docs/scope-change-rules A6): stacking resolution. Winner selection and limit-guarding
// stay in one collaborator deliberately — a limit check decides whether a rule *can* win, so
// splitting the guard out separately would mean threading mutable "remaining limit" state
// between two objects, which is worse.
//
// CR-02: TransferRule/ReversalRule never reach this selector — RuleEngine routes them to their
// own dedicated processors before calling SelectAsync. Every rule this selector DOES see is
// assumed to have a non-null TargetAccountTypeId.
//
// Algorithm (A6): partition exclusive[]/stackable[] -> resolve one winner per exclusivityGroup
// (priority DESC, ruleId ASC tiebreak) -> base[] = winners -> split stackable into
// multiplier[]/additive[] -> per wallet: total = sum(base) * product(multipliers) + sum(additives).
// Multiplication distributes over the base sum, so each base winner's OWN delta is scaled by
// the wallet's combined multiplier factor before posting (rather than posting one lump sum) —
// mathematically identical to A6's pseudocode, but keeps the existing one-ledger-entry-per-rule
// posting/audit granularity every other rule type here already relies on.
public sealed class WinnerSelector(
    IRuleTypeHandlerRegistry ruleTypeHandlers,
    ILimitCacheService limitCache,
    IRuleLimitEvaluator limitEvaluator,
    ILogger<WinnerSelector> logger) : IWinnerSelector
{
    // A10 guarantee #9: persisted per posting via AppliedRule.ResolutionSnapshot ->
    // RuleFireAudit.ResolutionSnapshot.
    private sealed record ResolutionSnapshot(
        string? ExclusivityGroup,
        IReadOnlyList<Guid> LosingRuleIds,
        IReadOnlyList<Guid> MultiplierRuleIdsInOrder,
        decimal MultiplierFactor,
        decimal RunningTotalBeforeMultiplier,
        decimal RunningTotalAfterMultiplier);

    public async Task<IReadOnlyList<AppliedRule>> SelectAsync(
        string tenantId,
        IReadOnlyList<CachedRule> earnRules,
        EvaluationEvent evt,
        ConditionContext context,
        CancellationToken ct)
    {
        // Earn-category rules must produce a strictly positive delta to be worth a winner
        // slot/stacking slot. Burn/Adjust-category rules post negative or operator-signed
        // deltas, so they're only discarded on an exact zero (no effect at all) — see
        // docs/scope-change-rules A1.
        var category = EventTypes.Describe(evt.EventType)?.Category ?? EventCategory.Earn;
        bool HasNoEffect(decimal delta) => category == EventCategory.Earn ? delta <= 0 : delta == 0;

        var pipelineRules = earnRules
            .Where(r => RuleTypes.UsesWinnerSelectorPipeline(r.Type) && r.TargetAccountTypeId.HasValue)
            .ToList();

        // Step 1/2/3: resolve one winner per exclusivity group. A9/RulesValidators require every
        // exclusive rule to carry an explicit ExclusivityGroup, but this falls back to the
        // rule's own target account id when one is somehow missing (stale/pre-migration data, a
        // hand-built CachedRule) — A6's stated default ("exclusivity group defaults to the
        // target account name") — so two exclusive rules on DIFFERENT wallets can never
        // accidentally suppress each other by colliding in a shared null group.
        var baseWinners = new List<(CachedRule Rule, decimal Delta, List<Guid> LosingIds)>();
        foreach (var group in pipelineRules.Where(r => !r.Stackable)
            .GroupBy(r => r.ExclusivityGroup ?? r.TargetAccountTypeId!.Value.ToString()))
        {
            var ordered = group.OrderByDescending(r => r.Priority).ThenBy(r => r.Id).ToList();
            CachedRule? winner = null;
            var winnerDelta = 0m;

            foreach (var rule in ordered)
            {
                if (!GroupedConditionEvaluator.Evaluate(rule.Conditions, evt, context))
                {
                    logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] condition not met", tenantId, rule.Name);
                    continue;
                }

                // CR-07: minimum event amount, "evaluated pre-calculation."
                if (rule.Limits?.MinEventAmount.HasValue == true && evt.Amount < rule.Limits.MinEventAmount.Value)
                {
                    logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] below min_event_amount", tenantId, rule.Name);
                    continue;
                }

                var delta = ruleTypeHandlers.Resolve(rule.Type).Compute(rule.Calculation, evt, rule.TargetDecimals);
                delta = ClampMaxPerEvent(rule, delta); // CR-07: "clamp at calculation"
                if (HasNoEffect(delta))
                {
                    logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] zero delta", tenantId, rule.Name);
                    continue;
                }

                if (await IsLimitExhaustedAsync(tenantId, rule, evt, category, ct))
                    continue;

                winner = rule;
                winnerDelta = delta;
                logger.LogInformation("RuleEngine: [{Tenant}] rule WINNER [{Rule}] group={Group} p={Priority}",
                    tenantId, rule.Name, rule.ExclusivityGroup, rule.Priority);
                break;
            }

            if (winner is not null)
            {
                var losers = ordered.Where(r => r.Id != winner.Id).Select(r => r.Id).ToList();
                baseWinners.Add((winner, winnerDelta, losers));
            }
        }

        // Step 5: stackable rules always fire when matched — split into multiplier/additive.
        var matchedStackable = pipelineRules
            .Where(r => r.Stackable && GroupedConditionEvaluator.Evaluate(r.Conditions, evt, context))
            .ToList();
        var multipliers = matchedStackable.Where(r => r.StackMode == RuleStackMode.Multiplier).ToList();
        var additives = matchedStackable.Where(r => r.StackMode != RuleStackMode.Multiplier).ToList();

        var appliedRules = new List<AppliedRule>();

        // Step 6/7: resolve per wallet, independently.
        var wallets = baseWinners.Select(b => b.Rule.TargetAccountTypeId!.Value)
            .Concat(additives.Select(r => r.TargetAccountTypeId!.Value))
            .Concat(multipliers.Select(r => r.TargetAccountTypeId!.Value))
            .Distinct();

        foreach (var walletId in wallets)
        {
            var basesForWallet = baseWinners.Where(b => b.Rule.TargetAccountTypeId == walletId).ToList();
            var multipliersForWallet = multipliers
                .Where(r => r.TargetAccountTypeId == walletId)
                .OrderBy(r => r.Priority).ThenBy(r => r.Id)
                .ToList();

            // "A multiplier with no base in its wallet is a no-op."
            var factor = 1m;
            var appliedMultiplierIds = new List<Guid>();
            if (basesForWallet.Count > 0)
            {
                foreach (var m in multipliersForWallet)
                {
                    var mFactor = ruleTypeHandlers.Resolve(m.Type).Compute(m.Calculation, evt);
                    if (mFactor <= 0)
                    {
                        logger.LogInformation("RuleEngine: [{Tenant}] multiplier SKIP [{Rule}] non-positive factor", tenantId, m.Name);
                        continue;
                    }
                    factor *= mFactor;
                    appliedMultiplierIds.Add(m.Id);
                }
            }

            foreach (var (rule, delta, losers) in basesForWallet)
            {
                var scaledDelta = ApplyRounding(rule, delta * factor);
                var snapshot = new ResolutionSnapshot(
                    rule.ExclusivityGroup, losers, appliedMultiplierIds, factor, delta, scaledDelta);
                appliedRules.Add(new AppliedRule(rule, scaledDelta, walletId, JsonSerializer.Serialize(snapshot)));
            }

            foreach (var rule in additives.Where(r => r.TargetAccountTypeId == walletId).OrderBy(r => r.Priority).ThenBy(r => r.Id))
            {
                if (rule.Limits?.MinEventAmount.HasValue == true && evt.Amount < rule.Limits.MinEventAmount.Value)
                    continue;

                var rawDelta = ruleTypeHandlers.Resolve(rule.Type).Compute(rule.Calculation, evt, rule.TargetDecimals);
                rawDelta = ClampMaxPerEvent(rule, rawDelta);
                if (HasNoEffect(rawDelta)) continue;

                var clippedDelta = await ClipToLimitAsync(tenantId, rule, evt, category, rawDelta, ct);
                if (clippedDelta is null) continue; // limit already exhausted

                var roundedDelta = ApplyRounding(rule, clippedDelta.Value);
                var snapshot = new ResolutionSnapshot(null, Array.Empty<Guid>(), Array.Empty<Guid>(), 1m, roundedDelta, roundedDelta);
                appliedRules.Add(new AppliedRule(rule, roundedDelta, walletId, JsonSerializer.Serialize(snapshot)));
            }
        }

        return appliedRules;
    }

    // CR-08 (A8): "round once per account kind, post" (A6). Deliberately a no-op unless the
    // rule EXPLICITLY sets Configuration.Rounding — A8's stated default is "inherit from
    // program," but Programs has no default-rounding column yet (out of this CR's file set), so
    // silently defaulting to e.g. "down" here would change every existing/unconfigured rule's
    // output (a CASH FixedBonusRule of 2.5 would become 2) without anyone asking for that.
    // Explicit configuration always applies.
    //
    // 1.3.CL item 2 (§5 d): for Spend rules the target wallet's `decimals` sets the precision and
    // Rounding only the direction, so a 3-4 place wallet is not cut back to 2. Every other rule
    // type keeps the fixed 2 places (§5 g — out of scope).
    private static decimal ApplyRounding(CachedRule rule, decimal delta)
    {
        var places = rule.Type == RuleTypes.SpendRule ? Math.Clamp(rule.TargetDecimals, 0, 4) : 2;
        var scale = places switch { 0 => 1m, 1 => 10m, 2 => 100m, 3 => 1000m, _ => 10000m };
        return rule.Configuration?.Rounding switch
        {
            "down" => Math.Floor(delta * scale) / scale,
            "up" => Math.Ceiling(delta * scale) / scale,
            "nearest" => Math.Round(delta, places, MidpointRounding.AwayFromZero),
            _ => delta
        };
    }

    // CR-07: "max per event, clamp at calculation." Sign-aware — clamps magnitude, preserves
    // direction, so it behaves sensibly for both Earn (positive) and Burn/Adjust (negative) deltas.
    private static decimal ClampMaxPerEvent(CachedRule rule, decimal delta)
    {
        if (rule.Limits?.MaxPerEvent is not { } max) return delta;
        return delta switch
        {
            > 0 when delta > max => max,
            < 0 when -delta > max => -max,
            _ => delta
        };
    }

    // PerCustomerTotal/PerCustomerPerDay are Earn-shaped caps ("how much can this customer earn
    // from this rule") — unchanged from before CR-07. The CR-07 additions below (cooldown, max
    // customers, rule budget, per-customer-per-period) are checked regardless of category except
    // where individually noted.
    private async Task<bool> IsLimitExhaustedAsync(string tenantId, CachedRule rule, EvaluationEvent evt, EventCategory category, CancellationToken ct)
    {
        if (category == EventCategory.Earn && rule.Limits?.PerCustomerTotal.HasValue == true)
        {
            var total = await limitCache.GetTotalAsync(tenantId, rule.Id, evt.ContactKey, ct);
            if (total >= rule.Limits.PerCustomerTotal.Value)
            {
                logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] per_customer_total exhausted", tenantId, rule.Name);
                return true;
            }
        }
        if (category == EventCategory.Earn && rule.Limits?.PerCustomerPerDay.HasValue == true)
        {
            var daily = await limitCache.GetDailyAsync(tenantId, rule.Id, evt.ContactKey, ct);
            if (daily >= rule.Limits.PerCustomerPerDay.Value)
            {
                logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] per_customer_per_day exhausted", tenantId, rule.Name);
                return true;
            }
        }

        if (rule.Limits is null) return false;

        if (rule.Limits.CooldownHours.HasValue &&
            await limitEvaluator.IsCooldownActiveAsync(tenantId, rule.Id, evt.ContactKey, rule.Limits.CooldownHours.Value, ct))
        {
            logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] cooldown active", tenantId, rule.Name);
            return true;
        }

        if (rule.Limits.MaxCustomers.HasValue &&
            await limitEvaluator.IsNewCustomerBlockedByMaxAsync(tenantId, rule.Id, evt.ContactKey, rule.Limits.MaxCustomers.Value, ct))
        {
            logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] max_customers reached", tenantId, rule.Name);
            return true;
        }

        if (rule.Limits.RuleBudgetTotal.HasValue)
        {
            var used = await limitEvaluator.GetRuleBudgetUsedAsync(tenantId, rule.Id, null, null, ct);
            if (used >= rule.Limits.RuleBudgetTotal.Value)
            {
                logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] rule_budget_total exhausted", tenantId, rule.Name);
                return true;
            }
        }
        if (rule.Limits.RuleBudgetPerPeriod.HasValue)
        {
            var used = await limitEvaluator.GetRuleBudgetUsedAsync(tenantId, rule.Id, rule.Limits.Period, rule.Limits.ResetWindow, ct);
            if (used >= rule.Limits.RuleBudgetPerPeriod.Value)
            {
                logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] rule_budget_per_period exhausted", tenantId, rule.Name);
                return true;
            }
        }
        if (category == EventCategory.Earn && rule.Limits.PerCustomerPerPeriod.HasValue)
        {
            var used = await limitEvaluator.GetCustomerPeriodUsedAsync(tenantId, rule.Id, evt.ContactKey, rule.Limits.Period, rule.Limits.ResetWindow, ct);
            if (used >= rule.Limits.PerCustomerPerPeriod.Value)
            {
                logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] per_customer_per_period exhausted", tenantId, rule.Name);
                return true;
            }
        }

        return false;
    }

    // Additive stackable path only — the exclusive-winner path gates eligibility but has never
    // clipped (pre-existing behavior, unchanged by CR-06/CR-07). CR-07's new budget/period
    // limits DO clip here, per their on_breach="Clamp" default (on_breach="Skip" returns null
    // instead, same as the legacy fields already do implicitly).
    private async Task<decimal?> ClipToLimitAsync(string tenantId, CachedRule rule, EvaluationEvent evt, EventCategory category, decimal delta, CancellationToken ct)
    {
        if (rule.Limits is null) return delta;

        if (category == EventCategory.Earn && rule.Limits.PerCustomerTotal.HasValue)
        {
            var total = await limitCache.GetTotalAsync(tenantId, rule.Id, evt.ContactKey, ct);
            if (total >= rule.Limits.PerCustomerTotal.Value) return null;
            var remaining = rule.Limits.PerCustomerTotal.Value - total;
            if (delta > remaining) delta = remaining;
        }

        if (category == EventCategory.Earn && rule.Limits.PerCustomerPerDay.HasValue)
        {
            var daily = await limitCache.GetDailyAsync(tenantId, rule.Id, evt.ContactKey, ct);
            if (daily >= rule.Limits.PerCustomerPerDay.Value) return null;
            var remaining = rule.Limits.PerCustomerPerDay.Value - daily;
            if (delta > remaining) delta = remaining;
        }

        if (rule.Limits.CooldownHours.HasValue &&
            await limitEvaluator.IsCooldownActiveAsync(tenantId, rule.Id, evt.ContactKey, rule.Limits.CooldownHours.Value, ct))
            return null;

        if (rule.Limits.MaxCustomers.HasValue &&
            await limitEvaluator.IsNewCustomerBlockedByMaxAsync(tenantId, rule.Id, evt.ContactKey, rule.Limits.MaxCustomers.Value, ct))
            return null;

        var skip = rule.Limits.OnBreach == "Skip";

        if (rule.Limits.RuleBudgetTotal is { } budgetTotal)
        {
            var used = await limitEvaluator.GetRuleBudgetUsedAsync(tenantId, rule.Id, null, null, ct);
            if (used >= budgetTotal) return null;
            var remaining = budgetTotal - used;
            if (delta > remaining) { if (skip) return null; delta = remaining; }
        }
        if (rule.Limits.RuleBudgetPerPeriod is { } budgetPeriod)
        {
            var used = await limitEvaluator.GetRuleBudgetUsedAsync(tenantId, rule.Id, rule.Limits.Period, rule.Limits.ResetWindow, ct);
            if (used >= budgetPeriod) return null;
            var remaining = budgetPeriod - used;
            if (delta > remaining) { if (skip) return null; delta = remaining; }
        }
        if (category == EventCategory.Earn && rule.Limits.PerCustomerPerPeriod is { } perPeriod)
        {
            var used = await limitEvaluator.GetCustomerPeriodUsedAsync(tenantId, rule.Id, evt.ContactKey, rule.Limits.Period, rule.Limits.ResetWindow, ct);
            if (used >= perPeriod) return null;
            var remaining = perPeriod - used;
            if (delta > remaining) { if (skip) return null; delta = remaining; }
        }

        return delta;
    }
}
