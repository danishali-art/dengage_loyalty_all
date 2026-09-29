using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema.Entities;

namespace dEngage.Loyalty.RuleEngine.Processing;

public interface ITierContextLoader
{
    // ruleConditions: CachedRule.Conditions (grouped tree — Rules/Card Buckets).
    // campaignConditions: CachedCampaignConfig.Conditions (flat DSL — Streak). Kept as two
    // separate enumerables rather than one unified list because the two DSLs are different
    // types (see ConditionTree.cs remarks) — both still feed the same LatestEventAt preload.
    Task<RuleEvaluationContext> LoadAsync(
        string tenantId,
        Guid programId,
        string eventId,
        EvaluationEvent evt,
        IEnumerable<ConditionTree?> ruleConditions,
        IEnumerable<List<ConditionClause>?> campaignConditions,
        CancellationToken ct);
}

// QualifyingAccountTypeId: the program's tier-qualifying wallet (AccountType.IsTierQualifying,
// 1.3.CL item 1), or null when the program has no tier system. Not read from Program anymore.
public sealed record RuleEvaluationContext(Program? Program, ConditionContext Condition, Guid? QualifyingAccountTypeId = null);
