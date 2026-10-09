using System.Globalization;
using System.Text.Json;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// CR 2026-10-06 Phase 4 (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md §3.8):
// the wallet's decimals set the precision, the rule's rounding — or the program's
// default_rounding when the rule inherits (Down unless changed) — the direction; applied once, to
// Spend, Fixed bonus and Manual adjustment, before the limits.
public sealed class RoundingCr1006Tests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;

    public RoundingCr1006Tests()
    {
        _programId = _harness.AddProgram();
        _harness.LimitCache.Setup(c => c.GetTotalAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid ruleId, string contact, CancellationToken _) =>
                _harness.Db.LedgerEntries.AsNoTracking().Where(l => l.RuleId == ruleId && l.ContactKey == contact)
                    .Select(l => l.Delta).ToList().Sum());
    }

    public void Dispose() => _harness.Dispose();

    private Guid Wallet(int decimals) =>
        _harness.AddAccountType(_programId, "POINTS", $"Points{decimals}", config: $$"""{"decimals": {{decimals}}}""");

    private void ProgramRounding(string direction)
    {
        _harness.Db.Programs.Where(p => p.Id == _programId).ExecuteUpdate(s => s.SetProperty(p => p.DefaultRounding, direction));
        _harness.Db.ChangeTracker.Clear();
    }

    private void AddRule(string type, string trigger, Guid wallet, int decimals, decimal? factor = null, decimal? fixedValue = null,
        string? ruleRounding = null, RuleLimits? limits = null) =>
        _harness.AddRule(new CachedRule
        {
            Id = Guid.NewGuid(),
            ProgramId = _programId,
            Name = $"{type}-{wallet:N}",
            Type = type,
            Trigger = trigger,
            Calculation = new RuleCalculation { Factor = factor, FixedValue = fixedValue, Reason = "correction" },
            TargetAccountTypeId = wallet,
            TargetDecimals = decimals,
            Configuration = ruleRounding is null ? null : new RuleSettings { Rounding = ruleRounding },
            Limits = limits,
            Priority = 10,
            Stackable = false,
            StackMode = RuleStackMode.Additive,
            Version = 1
        });

    private Task ProcessAsync(string trigger, string eventId, string contact, decimal amount) =>
        _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, eventId, new EvaluationEvent
        {
            EventType = trigger,
            ContactKey = contact,
            Amount = amount,
            OccurredAt = DateTime.UtcNow,
            Data = JsonSerializer.SerializeToElement(new { contact_key = contact, amount = amount.ToString(CultureInfo.InvariantCulture) })
        }, CancellationToken.None);

    // The payouts SpendRuleHandler used to produce by rounding down itself (1.3.CL item 2) are
    // unchanged by default: 42.99 × 0.3 = 12.897.
    [Theory]
    [InlineData(0, "12")]
    [InlineData(1, "12.8")]
    [InlineData(2, "12.89")]
    [InlineData(3, "12.897")]
    [InlineData(4, "12.897")]
    public async Task By_default_a_spend_award_rounds_down_to_the_wallets_decimals(int decimals, string expected)
    {
        var wallet = Wallet(decimals);
        AddRule(RuleTypes.SpendRule, EventTypes.OrderCreated, wallet, decimals, factor: 0.3m);

        await ProcessAsync(EventTypes.OrderCreated, "e1", "c1", 42.99m);

        _harness.GetBalance("c1", wallet).Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(RoundingDirection.Down, "12.89")]
    [InlineData(RoundingDirection.Nearest, "12.90")]
    [InlineData(RoundingDirection.Up, "12.90")]
    public async Task A_rule_that_inherits_uses_the_programs_default_rounding(string programDirection, string expected)
    {
        ProgramRounding(programDirection);
        var wallet = Wallet(2);
        AddRule(RuleTypes.SpendRule, EventTypes.OrderCreated, wallet, 2, factor: 0.3m);

        await ProcessAsync(EventTypes.OrderCreated, "e1", "c2", 42.99m);

        _harness.GetBalance("c2", wallet).Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_rules_own_rounding_beats_the_programs_default()
    {
        ProgramRounding(RoundingDirection.Up);
        var wallet = Wallet(0);
        AddRule(RuleTypes.SpendRule, EventTypes.OrderCreated, wallet, 0, factor: 0.3m, ruleRounding: RoundingDirection.Down);

        await ProcessAsync(EventTypes.OrderCreated, "e1", "c3", 42.99m);

        _harness.GetBalance("c3", wallet).Should().Be(12m);
    }

    // Fixed bonuses now round to the wallet's decimals too (they used to keep fractions a wallet
    // can't hold, e.g. 25.555 points in a whole-point wallet).
    [Theory]
    [InlineData(RoundingDirection.Down, "25")]
    [InlineData(RoundingDirection.Nearest, "26")]
    [InlineData(RoundingDirection.Up, "26")]
    public async Task A_fixed_bonus_rounds_to_the_wallets_decimals_in_the_rules_direction(string direction, string expected)
    {
        var wallet = Wallet(0);
        AddRule(RuleTypes.FixedBonusRule, EventTypes.OrderCreated, wallet, 0, fixedValue: 25.555m, ruleRounding: direction);

        await ProcessAsync(EventTypes.OrderCreated, "e1", "c4", 10m);

        _harness.GetBalance("c4", wallet).Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    // Directions apply to the award's size: a debit rounds toward zero on Down, like a credit.
    [Theory]
    [InlineData(RoundingDirection.Down, "-10")]
    [InlineData(RoundingDirection.Up, "-11")]
    public async Task A_negative_manual_adjustment_rounds_by_size(string direction, string expected)
    {
        var wallet = Wallet(0);
        AddRule(RuleTypes.ManualAdjustmentRule, EventTypes.PointsAdjusted, wallet, 0, ruleRounding: direction);

        await ProcessAsync(EventTypes.PointsAdjusted, "e1", "c5", -10.55m);

        _harness.GetBalance("c5", wallet).Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    // Rounding happens before the limits, so rounding up can't push an award past a cap.
    [Fact]
    public async Task Rounding_up_never_pushes_an_award_past_a_per_customer_cap()
    {
        var wallet = Wallet(0);
        AddRule(RuleTypes.SpendRule, EventTypes.OrderCreated, wallet, 0, factor: 0.3m, ruleRounding: RoundingDirection.Up,
            limits: new RuleLimits { PerCustomerTotal = 20m });

        await ProcessAsync(EventTypes.OrderCreated, "e1", "c6", 42.99m); // 12.897 → 13
        await ProcessAsync(EventTypes.OrderCreated, "e2", "c6", 42.99m); // 13 doesn't fit 7 → 7

        _harness.GetBalance("c6", wallet).Should().Be(20m);
    }
}
