using System.Text.Json;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// 1.3.CL item 5 worked example: with named exclusivity groups and multipliers retired, exclusive
// rules compete per target wallet (highest priority wins), stackable rules always add on top, and
// wallets resolve independently. Complements StackingResolutionWorkedExampleTests, which still
// documents the CR-06 group/multiplier path the engine keeps until the clean-up CR.
public sealed class StackingWithoutGroupsCl13Tests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _points;
    private readonly Guid _bonusPoints;

    public StackingWithoutGroupsCl13Tests()
    {
        _programId = _harness.AddProgram();
        _points = _harness.AddAccountType(_programId, "POINTS", "Points");
        _bonusPoints = _harness.AddAccountType(_programId, "POINTS", "Bonus");

        // Two exclusive earn rates on the same wallet, no group: priority 20 must beat 10.
        AddRule("base 1x", _points, factor: 1m, priority: 10, stackable: false);
        AddRule("gold 2x", _points, factor: 2m, priority: 20, stackable: false);
        // A stackable fixed bonus adds on top of the winner in the same wallet.
        AddRule("weekend +50", _points, fixedValue: 50m, priority: 5, stackable: true, type: RuleTypes.FixedBonusRule);
        // An exclusive rule on a different wallet never competes with the Points wallet.
        AddRule("bonus 0.5x", _bonusPoints, factor: 0.5m, priority: 1, stackable: false);
    }

    public void Dispose() => _harness.Dispose();

    private void AddRule(string name, Guid target, int priority, bool stackable,
        decimal? factor = null, decimal? fixedValue = null, string type = RuleTypes.SpendRule) =>
        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(),
            ProgramId = _programId,
            Name = name,
            Type = type,
            Trigger = "remittance",
            Calculation = new RuleCalculation { Factor = factor, FixedValue = fixedValue },
            TargetAccountTypeId = target,
            Priority = priority,
            Stackable = stackable,
            ExclusivityGroup = null,
            StackMode = RuleStackMode.Additive,
            Version = 1
        });

    [Fact]
    public async Task Highest_priority_exclusive_rule_wins_per_wallet_and_stackables_add()
    {
        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-stack-1", new EvaluationEvent
        {
            EventType = "remittance",
            ContactKey = "stack_c",
            Amount = 100m,
            OccurredAt = DateTime.UtcNow,
            Data = JsonSerializer.SerializeToElement(new { contact_key = "stack_c", amount = "100.00" })
        }, CancellationToken.None);

        _harness.GetBalance("stack_c", _points).Should().Be(250m);      // gold 2x (200) + weekend 50; base 1x loses
        _harness.GetBalance("stack_c", _bonusPoints).Should().Be(50m);  // its own wallet, unaffected
    }
}
