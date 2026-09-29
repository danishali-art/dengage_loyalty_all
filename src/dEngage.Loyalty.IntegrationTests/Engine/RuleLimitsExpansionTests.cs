using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Moq;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// CR-07 (docs/scope-change-rules A7): the new limit types, exercised against the real
// (Sqlite-backed) ledger-query implementation, not a mock — these are correctness-critical
// money-adjacent gates.
public sealed class RuleLimitsExpansionTests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _pointsAccountTypeId;

    public RuleLimitsExpansionTests()
    {
        _programId = _harness.AddProgram();
        _pointsAccountTypeId = _harness.AddAccountType(_programId, "POINTS", "Points");

        _harness.LimitCache.Setup(c => c.GetTotalAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        _harness.LimitCache.Setup(c => c.GetDailyAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
    }

    public void Dispose() => _harness.Dispose();

    private static EvaluationEvent CardEvent(string contact, decimal amount, DateTime? occurredAt = null) => new()
    {
        EventType = "card.transaction",
        ContactKey = contact,
        Amount = amount,
        OccurredAt = occurredAt ?? DateTime.UtcNow,
        Data = System.Text.Json.JsonSerializer.SerializeToElement(new { contact_key = contact, amount = amount.ToString("F2") })
    };

    [Fact]
    public async Task Max_per_event_clamps_a_single_award_regardless_of_computed_delta()
    {
        var ruleId = Guid.NewGuid();
        _harness.AddRule(new CachedRule
        {
            Id = ruleId, ProgramId = _programId, Name = "Capped Spend",
            Type = RuleTypes.SpendRule, Trigger = "card.transaction",
            Calculation = new RuleCalculation { Factor = 1m }, // 1:1 — would earn 1000 uncapped
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100, Stackable = false, ExclusivityGroup = "g1",
            Limits = new RuleLimits { MaxPerEvent = 50m },
            Version = 1
        });

        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("c1", 1000m), CancellationToken.None);

        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(50m);
    }

    [Fact]
    public async Task Min_event_amount_blocks_the_rule_entirely_below_the_floor()
    {
        var ruleId = Guid.NewGuid();
        _harness.AddRule(new CachedRule
        {
            Id = ruleId, ProgramId = _programId, Name = "Min Amount Rule",
            Type = RuleTypes.FixedBonusRule, Trigger = "card.transaction",
            Calculation = new RuleCalculation { FixedValue = 10m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100, Stackable = false, ExclusivityGroup = "g1",
            Limits = new RuleLimits { MinEventAmount = 100m },
            Version = 1
        });

        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("c1", 50m), CancellationToken.None);
        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(0m);

        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-2", CardEvent("c1", 150m), CancellationToken.None);
        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(10m);
    }

    [Fact]
    public async Task Cooldown_blocks_a_repeat_award_within_the_window()
    {
        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(), ProgramId = _programId, Name = "Cooldown Rule",
            Type = RuleTypes.FixedBonusRule, Trigger = "card.transaction",
            Calculation = new RuleCalculation { FixedValue = 10m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100, Stackable = false, ExclusivityGroup = "g1",
            Limits = new RuleLimits { CooldownHours = 24m },
            Version = 1
        });

        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("c1", 100m), CancellationToken.None);
        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(10m);

        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-2", CardEvent("c1", 100m), CancellationToken.None);
        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(10m, "still within the cooldown window");

        // Cooldown compares against LedgerEntry.CreatedAt (real wall-clock posting time), not
        // EvaluationEvent.OccurredAt — backdate the row directly to simulate 25 elapsed hours
        // rather than actually sleeping the test.
        var lastEntry = _harness.Db.LedgerEntries.OrderByDescending(x => x.CreatedAt).First(x => x.ContactKey == "c1");
        lastEntry.CreatedAt = DateTime.UtcNow.AddHours(-25);
        _harness.Db.SaveChanges();

        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-3", CardEvent("c1", 100m), CancellationToken.None);
        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(20m, "cooldown has elapsed");
    }

    [Fact]
    public async Task Rule_budget_total_clamps_spend_across_every_customer()
    {
        // Stackable/additive — RuleBudgetTotal only clamps on the additive path (the
        // exclusive-winner path gates eligibility but has never clipped, same pre-existing
        // limitation PerCustomerTotal/PerCustomerPerDay already had before CR-07).
        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(), ProgramId = _programId, Name = "Budget Rule",
            Type = RuleTypes.FixedBonusRule, Trigger = "card.transaction",
            Calculation = new RuleCalculation { FixedValue = 30m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100, Stackable = true, StackMode = RuleStackMode.Additive,
            Limits = new RuleLimits { RuleBudgetTotal = 50m, OnBreach = "Clamp" },
            Version = 1
        });

        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("c1", 100m), CancellationToken.None);
        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(30m);

        // Second customer would earn 30 more (60 total), but only 20 of budget remains -> clamped.
        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-2", CardEvent("c2", 100m), CancellationToken.None);
        _harness.GetBalance("c2", _pointsAccountTypeId).Should().Be(20m);

        // Budget now fully exhausted (50/50) — a third customer earns nothing.
        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-3", CardEvent("c3", 100m), CancellationToken.None);
        _harness.GetBalance("c3", _pointsAccountTypeId).Should().Be(0m);
    }
}
