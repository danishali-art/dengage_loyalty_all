using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared.Events;
using Microsoft.Extensions.Logging;

namespace dEngage.Loyalty.RuleEngine.Processing;

// CR 2026-10-05 item 1 — see IBurnRuleResolver. Eligibility mirrors WinnerSelector's exclusive
// path for non-Earn rules (conditions, min_event_amount, cooldown, max_customers, rule budget);
// the Earn-only caps (per_customer_total/per_day/per_period) don't apply to a burn, same as there.
public sealed class BurnRuleResolver(
    IRuleCacheService ruleCache,
    ITierContextLoader tierContextLoader,
    IRuleLimitEvaluator limitEvaluator,
    IBudgetReservationService budgetReservation,
    IRuleFireAuditWriter auditWriter,
    ILogger<BurnRuleResolver> logger) : IBurnRuleResolver
{
    public async Task<BurnRuleResolution> ResolveAsync(
        string tenantId,
        Guid programId,
        string ruleType,
        Guid sourceAccountTypeId,
        string eventId,
        EvaluationEvent evt,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var candidates = (await ruleCache.GetRulesAsync(tenantId, programId, ct))
            .Where(r => r.Type == ruleType &&
                        r.Trigger == evt.EventType &&
                        r.TargetAccountTypeId == sourceAccountTypeId &&
                        (r.ActiveFrom == null || r.ActiveFrom <= now) &&
                        (r.ActiveTo == null || now < r.ActiveTo))
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToList();

        if (candidates.Count == 0)
        {
            logger.LogInformation("BurnRule: [{Tenant}] no {Type} targets account type {AccountType}",
                tenantId, ruleType, sourceAccountTypeId);
            return BurnRuleResolution.Failed(OutcomeReasons.NoRule);
        }

        var context = await tierContextLoader.LoadAsync(
            tenantId, programId, eventId, evt,
            candidates.Select(r => r.Conditions),
            Array.Empty<List<ConditionClause>?>(),
            ct);

        var conditionsMet = false;
        foreach (var rule in candidates)
        {
            if (!GroupedConditionEvaluator.Evaluate(rule.Conditions, evt, context.Condition))
            {
                logger.LogInformation("BurnRule: [{Tenant}] rule SKIP  [{Rule}] condition not met", tenantId, rule.Name);
                continue;
            }
            conditionsMet = true;

            if (await IsLimitExhaustedAsync(tenantId, rule, evt, ct))
                continue;

            logger.LogInformation("BurnRule: [{Tenant}] rule WINNER [{Rule}] p={Priority}", tenantId, rule.Name, rule.Priority);
            return BurnRuleResolution.Found(rule);
        }

        return BurnRuleResolution.Failed(conditionsMet
            ? OutcomeReasons.RuleLimitReached
            : OutcomeReasons.NoRule);
    }

    public async Task<bool> TryReserveBudgetAsync(string tenantId, CachedRule rule, decimal amount, CancellationToken ct)
    {
        if (rule.Limits is not { } limits) return true;

        // Check every budget before recording any: on a refusal the caller still commits (to
        // write redeem_failed / transfer_failed), so a usage recorded for one budget before
        // another refused would be kept for a burn that never happened.
        var lockedPeriodKey = (string?)null;
        if (limits.RuleBudgetTotal is { } total)
        {
            var used = await budgetReservation.LockAndGetUsageAsync(tenantId, rule.Id, BudgetReservationService.BudgetTotal, "", null, ct);
            if (used + amount > total) return false;
        }

        if (limits.RuleBudgetPerPeriod is { } perPeriod)
        {
            if (limits.ResetWindow == "Rolling")
            {
                // A rolling window has no discrete bucket to lock (see LedgerPoster) — checked
                // against ledger history only, like the engine's pre-check.
                var used = await limitEvaluator.GetRuleBudgetUsedAsync(tenantId, rule.Id, limits.Period, limits.ResetWindow, ct);
                if (used + amount > perPeriod) return false;
            }
            else
            {
                lockedPeriodKey = PeriodWindow.CalendarKey(limits.Period, DateTime.UtcNow);
                var used = await budgetReservation.LockAndGetUsageAsync(tenantId, rule.Id, BudgetReservationService.BudgetPeriod, lockedPeriodKey, limits.Period, ct);
                if (used + amount > perPeriod) return false;
            }
        }

        if (limits.RuleBudgetTotal.HasValue)
            await budgetReservation.RecordUsageAsync(tenantId, rule.Id, BudgetReservationService.BudgetTotal, "", amount, ct);
        if (lockedPeriodKey is not null)
            await budgetReservation.RecordUsageAsync(tenantId, rule.Id, BudgetReservationService.BudgetPeriod, lockedPeriodKey, amount, ct);

        return true;
    }

    public Task RecordFireAsync(
        string tenantId,
        CachedRule rule,
        string eventId,
        string contactKey,
        decimal delta,
        Guid? ledgerEntryId,
        CancellationToken ct) =>
        auditWriter.RecordAsync(
            tenantId, rule.Id, rule.Version, eventId, contactKey,
            rule.Conditions, rule.Calculation, delta, ledgerEntryId, resolutionSnapshot: null, ct);

    private async Task<bool> IsLimitExhaustedAsync(string tenantId, CachedRule rule, EvaluationEvent evt, CancellationToken ct)
    {
        if (rule.Limits is not { } limits) return false;

        if (limits.MinEventAmount.HasValue && evt.Amount < limits.MinEventAmount.Value)
        {
            logger.LogInformation("BurnRule: [{Tenant}] rule SKIP  [{Rule}] below min_event_amount", tenantId, rule.Name);
            return true;
        }

        if (limits.CooldownHours.HasValue &&
            await limitEvaluator.IsCooldownActiveAsync(tenantId, rule.Id, evt.ContactKey, limits.CooldownHours.Value, ct))
        {
            logger.LogInformation("BurnRule: [{Tenant}] rule SKIP  [{Rule}] cooldown active", tenantId, rule.Name);
            return true;
        }

        if (limits.MaxCustomers.HasValue &&
            await limitEvaluator.IsNewCustomerBlockedByMaxAsync(tenantId, rule.Id, evt.ContactKey, limits.MaxCustomers.Value, ct))
        {
            logger.LogInformation("BurnRule: [{Tenant}] rule SKIP  [{Rule}] max_customers reached", tenantId, rule.Name);
            return true;
        }

        if (limits.RuleBudgetTotal.HasValue &&
            await limitEvaluator.GetRuleBudgetUsedAsync(tenantId, rule.Id, null, null, ct) >= limits.RuleBudgetTotal.Value)
        {
            logger.LogInformation("BurnRule: [{Tenant}] rule SKIP  [{Rule}] rule_budget_total exhausted", tenantId, rule.Name);
            return true;
        }

        if (limits.RuleBudgetPerPeriod.HasValue &&
            await limitEvaluator.GetRuleBudgetUsedAsync(tenantId, rule.Id, limits.Period, limits.ResetWindow, ct) >= limits.RuleBudgetPerPeriod.Value)
        {
            logger.LogInformation("BurnRule: [{Tenant}] rule SKIP  [{Rule}] rule_budget_per_period exhausted", tenantId, rule.Name);
            return true;
        }

        return false;
    }
}
