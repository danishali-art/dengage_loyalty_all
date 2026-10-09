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
        CancellationToken ct,
        string? programDefaultRounding = null)
    {
        // CR 2026-10-06 Phase 4: the one rounding step, applied right after the calculation and
        // the max-per-event cap and before the limits, so a cap trims an already-rounded award
        // and is never pushed over by rounding up afterwards.
        decimal Round(CachedRule rule, decimal delta) => ApplyRounding(rule, delta, programDefaultRounding);

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
                delta = Round(rule, ClampMaxPerEvent(rule, delta)); // CR-07: "clamp at calculation"
                if (HasNoEffect(delta))
                {
                    logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] zero delta", tenantId, rule.Name);
                    continue;
                }

                if (await AlreadyAwardedOnceAsync(tenantId, rule, evt, ct))
                    continue;

                // CR 2026-10-06 D6/D13: the same cap check as stackable rules. The award is trimmed
                // to what the caps leave (On breach = Clamp) or the rule is skipped (Skip, or a cap
                // already used up) — then the next exclusive rule gets its chance. Before, an
                // exclusive rule only checked "already at the cap?" and could pay past it.
                var clipped = await ClipToLimitAsync(tenantId, rule, evt, category, delta, ct);
                if (clipped is null || HasNoEffect(clipped.Value))
                {
                    logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] limit reached", tenantId, rule.Name);
                    continue;
                }

                winner = rule;
                winnerDelta = clipped.Value;
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
                var scaledDelta = Round(rule, delta * factor); // a no-op unless a legacy multiplier applied
                var snapshot = new ResolutionSnapshot(
                    rule.ExclusivityGroup, losers, appliedMultiplierIds, factor, delta, scaledDelta);
                appliedRules.Add(new AppliedRule(rule, scaledDelta, walletId, JsonSerializer.Serialize(snapshot)));
            }

            foreach (var rule in additives.Where(r => r.TargetAccountTypeId == walletId).OrderBy(r => r.Priority).ThenBy(r => r.Id))
            {
                if (rule.Limits?.MinEventAmount.HasValue == true && evt.Amount < rule.Limits.MinEventAmount.Value)
                    continue;

                var rawDelta = ruleTypeHandlers.Resolve(rule.Type).Compute(rule.Calculation, evt, rule.TargetDecimals);
                rawDelta = Round(rule, ClampMaxPerEvent(rule, rawDelta));
                if (HasNoEffect(rawDelta)) continue;

                if (await AlreadyAwardedOnceAsync(tenantId, rule, evt, ct)) continue;

                var clippedDelta = await ClipToLimitAsync(tenantId, rule, evt, category, rawDelta, ct);
                if (clippedDelta is null || HasNoEffect(clippedDelta.Value)) continue; // limit reached

                var roundedDelta = clippedDelta.Value;
                var snapshot = new ResolutionSnapshot(null, Array.Empty<Guid>(), Array.Empty<Guid>(), 1m, roundedDelta, roundedDelta);
                appliedRules.Add(new AppliedRule(rule, roundedDelta, walletId, JsonSerializer.Serialize(snapshot)));
            }
        }

        return appliedRules;
    }

    // CR-08 (A8) "round once per account kind, post", completed by CR 2026-10-06 Phase 4 (§3.8):
    // the target wallet's decimals set the precision for every pipeline rule type (Spend, Fixed
    // bonus, Manual adjustment — it used to be the wallet's for Spend and a fixed 2 places for the
    // others), and the rule's Configuration.rounding — or, when unset, the program's
    // default_rounding (Down unless changed) — sets the direction. Down reproduces the rounding
    // Spend always had, so payouts only change once someone picks Nearest or Up. The direction
    // applies to the award's size: a negative adjustment rounds toward zero on Down, as a
    // positive one does. "Nearest" is half away from zero. Rounding twice with the same settings
    // changes nothing, so the second call after a multiplier is safe.
    private static decimal ApplyRounding(CachedRule rule, decimal delta, string? programDefaultRounding)
    {
        var places = Math.Clamp(rule.TargetDecimals, 0, 4);
        var scale = places switch { 0 => 1m, 1 => 10m, 2 => 100m, 3 => 1000m, _ => 10000m };
        var direction = rule.Configuration?.Rounding ?? programDefaultRounding ?? RoundingDirection.Down;
        var size = Math.Abs(delta) * scale;
        var rounded = direction switch
        {
            RoundingDirection.Up => Math.Ceiling(size),
            RoundingDirection.Nearest => Math.Round(size, MidpointRounding.AwayFromZero),
            _ => Math.Floor(size)
        } / scale;
        return delta < 0 ? -rounded : rounded;
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

    // CR 2026-10-06 D12: signup / kyc.completed pay each rule at most once per customer
    // (OnceOnlyAward). Checked before the caps, so a lower-priority exclusive rule still gets its
    // chance; LedgerPoster re-checks inside its transaction and posts under the once key.
    private async Task<bool> AlreadyAwardedOnceAsync(string tenantId, CachedRule rule, EvaluationEvent evt, CancellationToken ct)
    {
        if (!OnceOnlyAward.Applies(evt.EventType) ||
            !await limitEvaluator.HasOnceOnlyAwardAsync(tenantId, rule.Id, evt.ContactKey, ct))
            return false;

        logger.LogInformation("RuleEngine: [{Tenant}] rule SKIP  [{Rule}] {Reason}", tenantId, rule.Name, OnceOnlyAward.SkipReason);
        return true;
    }

    // The one cap check for exclusive and stackable rules (CR 2026-10-06 D6/D13 — exclusive rules
    // used to check only "already at the cap?" and could pay past it). Returns the award trimmed to
    // what every cap leaves, or null to skip the rule. A cap already used up, a cooldown or max
    // customers always skips; a cap the award would exceed trims it (On breach = Clamp, the
    // default) or skips the rule (Skip). PerCustomerTotal/PerCustomerPerDay/PerCustomerPerPeriod
    // are Earn-shaped caps ("how much can this customer earn from this rule"); the rest apply to
    // every category.
    private async Task<decimal?> ClipToLimitAsync(string tenantId, CachedRule rule, EvaluationEvent evt, EventCategory category, decimal delta, CancellationToken ct)
    {
        if (rule.Limits is null) return delta;

        var skip = rule.Limits.OnBreach == "Skip";

        if (category == EventCategory.Earn && rule.Limits.PerCustomerTotal.HasValue)
        {
            var total = await limitCache.GetTotalAsync(tenantId, rule.Id, evt.ContactKey, ct);
            if (total >= rule.Limits.PerCustomerTotal.Value) return null;
            var remaining = rule.Limits.PerCustomerTotal.Value - total;
            if (delta > remaining) { if (skip) return null; delta = remaining; }
        }

        if (category == EventCategory.Earn && rule.Limits.PerCustomerPerDay.HasValue)
        {
            var daily = await limitCache.GetDailyAsync(tenantId, rule.Id, evt.ContactKey, ct);
            if (daily >= rule.Limits.PerCustomerPerDay.Value) return null;
            var remaining = rule.Limits.PerCustomerPerDay.Value - daily;
            if (delta > remaining) { if (skip) return null; delta = remaining; }
        }

        if (rule.Limits.CooldownHours.HasValue &&
            await limitEvaluator.IsCooldownActiveAsync(tenantId, rule.Id, evt.ContactKey, rule.Limits.CooldownHours.Value, ct))
            return null;

        if (rule.Limits.MaxCustomers.HasValue &&
            await limitEvaluator.IsNewCustomerBlockedByMaxAsync(tenantId, rule.Id, evt.ContactKey, rule.Limits.MaxCustomers.Value, ct))
            return null;

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
