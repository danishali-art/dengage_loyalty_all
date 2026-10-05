using System.Text.Json;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Moq;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// CR-06 (docs/scope-change-rules A6) golden test — the doc's own worked reference case:
// "500 SAR grocery transaction, grocery 3% (priority 200) beats base 1% (priority 100) in
// group earn-rate; monthly bonus 200 wins group one-off; weekend x2 and birthday +50 stack;
// coffee stamp wins group stamp-card. Result: ((15 + 200) x 2) + 50 = 480 points, 1 stamp."
// The stamp leg was dropped when stamps were retired (CR 2026-10-05); it posted to a separate
// wallet, so the 480-point result is unchanged.
public sealed class StackingResolutionWorkedExampleTests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _pointsAccountTypeId;

    public StackingResolutionWorkedExampleTests()
    {
        _programId = _harness.AddProgram();
        _pointsAccountTypeId = _harness.AddAccountType(_programId, "POINTS", "Points");

        _harness.LimitCache.Setup(c => c.GetTotalAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        _harness.LimitCache.Setup(c => c.GetDailyAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        // earn-rate group: grocery 3% (higher priority) beats base 1%.
        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(), ProgramId = _programId, Name = "Grocery Rate",
            Type = RuleTypes.SpendRule, Trigger = "card.transaction",
            Conditions = JsonSerializer.Deserialize<ConditionTree>("""
                {"op":"AND","groups":[{"op":"AND","conditions":[
                    {"field":"mcc","operator":"eq","value":{"type":"string","data":"grocery"}}
                ]}]}
                """),
            Calculation = new RuleCalculation { Factor = 0.03m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 200, Stackable = false, ExclusivityGroup = "earn-rate",
            Version = 1
        });
        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(), ProgramId = _programId, Name = "Base Rate",
            Type = RuleTypes.SpendRule, Trigger = "card.transaction",
            Calculation = new RuleCalculation { Factor = 0.01m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100, Stackable = false, ExclusivityGroup = "earn-rate",
            Version = 1
        });

        // one-off group: monthly bonus wins alone.
        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(), ProgramId = _programId, Name = "Monthly Bonus",
            Type = RuleTypes.FixedBonusRule, Trigger = "card.transaction",
            Calculation = new RuleCalculation { FixedValue = 200m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100, Stackable = false, ExclusivityGroup = "one-off",
            Version = 1
        });

        // Stackable: weekend x2 multiplier + birthday +50 additive.
        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(), ProgramId = _programId, Name = "Weekend Multiplier",
            Type = RuleTypes.FixedBonusRule, Trigger = "card.transaction",
            Conditions = JsonSerializer.Deserialize<ConditionTree>("""
                {"op":"AND","groups":[{"op":"AND","conditions":[
                    {"field":"event.day_of_week","operator":"in","value":{"type":"string[]","data":["saturday","sunday"]}}
                ]}]}
                """),
            Calculation = new RuleCalculation { FixedValue = 2m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100, Stackable = true, StackMode = RuleStackMode.Multiplier,
            Version = 1
        });
        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(), ProgramId = _programId, Name = "Birthday Bonus",
            Type = RuleTypes.FixedBonusRule, Trigger = "card.transaction",
            Calculation = new RuleCalculation { FixedValue = 50m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100, Stackable = true, StackMode = RuleStackMode.Additive,
            Version = 1
        });
    }

    public void Dispose() => _harness.Dispose();

    private static DateTime NextSaturday(DateTime from)
    {
        var d = from;
        while (d.DayOfWeek != DayOfWeek.Saturday) d = d.AddDays(1);
        return d;
    }

    [Fact]
    public async Task Worked_reference_case_yields_480_points()
    {
        var occurredAt = NextSaturday(DateTime.UtcNow).Date.AddHours(12);
        var evt = new EvaluationEvent
        {
            EventType = "card.transaction",
            ContactKey = "grocery_shopper",
            Amount = 500m,
            OccurredAt = occurredAt,
            Data = JsonSerializer.SerializeToElement(new { contact_key = "grocery_shopper", amount = "500.00", mcc = "grocery" })
        };

        await _harness.Engine.ProcessEventAsync(
            RuleEngineTestHarness.TenantSlug, _programId, "evt-grocery", evt, CancellationToken.None);

        _harness.GetBalance("grocery_shopper", _pointsAccountTypeId).Should().Be(480m);
    }
}
