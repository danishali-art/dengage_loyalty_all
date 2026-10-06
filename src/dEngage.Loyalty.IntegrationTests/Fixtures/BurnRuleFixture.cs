using System.Text.Json;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace dEngage.Loyalty.IntegrationTests.Fixtures;

// CR 2026-10-05: redeem / transfer handlers pick their rule through IBurnRuleResolver. The E2E
// tests run the real resolver (conditions, limits, budget reservation, audit) against Postgres;
// only the Redis-backed rule cache is replaced by an in-memory list, as RuleEngineTestHarness does.
internal sealed class BurnRuleFixture
{
    private readonly List<CachedRule> _rules = new();
    private readonly Mock<IRuleCacheService> _cache = new();

    public BurnRuleFixture()
    {
        // Like RuleCacheService: Active rules of the program only (callers add only Active ones).
        _cache.Setup(c => c.GetRulesAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid programId, CancellationToken _) => _rules.Where(r => r.ProgramId == programId).ToList());
    }

    public IBurnRuleResolver Resolver(LoyaltyDbContext db, ITenantSlugResolver tenantSlugResolver)
    {
        var limitEvaluator = new RuleLimitEvaluator(db);
        return new BurnRuleResolver(
            _cache.Object,
            new TierContextLoader(db, tenantSlugResolver),
            limitEvaluator,
            new BudgetReservationService(db, tenantSlugResolver, limitEvaluator),
            new RuleFireAuditWriter(db, tenantSlugResolver),
            NullLogger<BurnRuleResolver>.Instance);
    }

    public static CachedRule Redemption(Guid programId, Guid pointsId, Guid cashId, decimal rate, decimal? minRedeem = null,
        int priority = 10, RuleLimits? limits = null, ConditionTree? conditions = null) => new()
    {
        Id = Guid.NewGuid(),
        ProgramId = programId,
        Name = $"redeem-{Guid.NewGuid():N}",
        Type = RuleTypes.RedemptionRule,
        Trigger = EventTypes.PointsRedeem,
        TargetAccountTypeId = pointsId,
        Calculation = new RuleCalculation { Factor = rate, MinRedeem = minRedeem, CashAccountTypeId = cashId },
        Priority = priority,
        Limits = limits,
        Conditions = conditions,
        Version = 1
    };

    public static CachedRule Transfer(Guid programId, Guid pointsId, decimal maxPerDay, int priority = 10,
        RuleLimits? limits = null, ConditionTree? conditions = null) => new()
    {
        Id = Guid.NewGuid(),
        ProgramId = programId,
        Name = $"transfer-{Guid.NewGuid():N}",
        Type = RuleTypes.TransferRule,
        Trigger = EventTypes.PointsTransfer,
        TargetAccountTypeId = pointsId,
        Calculation = new RuleCalculation { MaxPerDay = maxPerDay },
        Priority = priority,
        Limits = limits,
        Conditions = conditions,
        Version = 1
    };

    // ledger_entries.rule_id has a real FK to rules, so the row must exist too.
    public async Task<CachedRule> AddAsync(LoyaltyDbContext db, Guid tenantGuid, CachedRule rule)
    {
        db.Rules.Add(new Rule
        {
            Id = rule.Id,
            TenantId = tenantGuid,
            ProgramId = rule.ProgramId,
            Name = rule.Name,
            Type = rule.Type,
            Trigger = rule.Trigger,
            Conditions = rule.Conditions is null ? null : JsonSerializer.Serialize(rule.Conditions),
            Calculation = JsonSerializer.Serialize(rule.Calculation),
            TargetAccountTypeId = rule.TargetAccountTypeId,
            Limits = rule.Limits is null ? null : JsonSerializer.Serialize(rule.Limits),
            Priority = rule.Priority,
            Status = RuleStatus.Active,
            CurrentVersion = rule.Version,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        _rules.Add(rule);
        return rule;
    }
}
