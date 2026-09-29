using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using Microsoft.EntityFrameworkCore;

namespace dEngage.Loyalty.RuleEngine.Processing;

public sealed class TierContextLoader(LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver) : ITierContextLoader
{
    public async Task<RuleEvaluationContext> LoadAsync(
        string tenantId,
        Guid programId,
        string eventId,
        EvaluationEvent evt,
        IEnumerable<ConditionTree?> ruleConditions,
        IEnumerable<List<ConditionClause>?> campaignConditions,
        CancellationToken ct)
    {
        // Read the customer's current tier name (before rule evaluation — does not count this event's earnings)
        var program = await db.Programs
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == programId, ct);

        var tenantGuid = await tenantSlugResolver.ResolveAsync(tenantId, ct);

        // 1.3.CL item 1: the qualifying wallet is flagged on the account type, not on the program.
        var qualifyingAccountTypeId = await db.AccountTypes
            .AsNoTracking()
            .Where(a => a.TenantId == tenantGuid && a.ProgramId == programId && a.IsTierQualifying)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        string? customerTierName = null;
        if (qualifyingAccountTypeId is not null)
        {
            customerTierName = await db.CustomerAccounts
                .AsNoTracking()
                .Where(ca =>
                    ca.TenantId == tenantGuid &&
                    ca.ContactKey == evt.ContactKey &&
                    ca.AccountTypeId == qualifyingAccountTypeId)
                .Select(ca => ca.Tier != null ? ca.Tier.Name : null)
                .FirstOrDefaultAsync(ct);
        }

        var context = new ConditionContext
        {
            CustomerTierName = customerTierName,
            LatestEventAt = await LoadLatestEventTimesAsync(tenantId, eventId, evt, ruleConditions, campaignConditions, ct)
        };

        return new RuleEvaluationContext(program, context, qualifyingAccountTypeId);
    }

    private const string HoursSincePrefix = "agg.hoursSince.";

    private async Task<IReadOnlyDictionary<string, DateTime>> LoadLatestEventTimesAsync(
        string tenantId,
        string eventId,
        EvaluationEvent evt,
        IEnumerable<ConditionTree?> ruleConditions,
        IEnumerable<List<ConditionClause>?> campaignConditions,
        CancellationToken ct)
    {
        var windowTypes = new HashSet<string>();

        // Flat DSL (Streak): occurred_within clauses.
        foreach (var conditions in campaignConditions)
        {
            if (conditions is null) continue;
            foreach (var clause in conditions)
                if (clause.Op == ConditionOps.OccurredWithin &&
                    OccurredWithinValue.TryParse(clause.Value, out var window))
                    windowTypes.Add(window.AfterEvent);
        }

        // Grouped tree (Rules/Card Buckets): agg.hoursSince.<eventType> fields — CR-05's
        // replacement for occurred_within, see ConditionTree.cs/GroupedConditionEvaluator.
        foreach (var tree in ruleConditions)
        {
            if (tree is null) continue;
            foreach (var group in tree.Groups)
                foreach (var leaf in group.Conditions)
                    if (leaf.Field.StartsWith(HoursSincePrefix, StringComparison.Ordinal))
                        windowTypes.Add(leaf.Field[HoursSincePrefix.Length..]);
        }

        if (windowTypes.Count == 0)
            return ConditionContext.Empty.LatestEventAt;

        // The current event's own log row is written before dispatch — exclude it so a
        // rule like "kyc.completed within 1h of kyc.completed" can't match itself.
        var latest = await db.EventLog
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId &&
                        e.ContactKey == evt.ContactKey &&
                        windowTypes.Contains(e.EventType) &&
                        e.EventId != eventId)
            .GroupBy(e => e.EventType)
            .Select(g => new { EventType = g.Key, LastAt = g.Max(e => e.OccurredAt) })
            .ToListAsync(ct);

        return latest.ToDictionary(x => x.EventType, x => x.LastAt);
    }
}
