using FluentAssertions;
using dEngage.Loyalty.RuleEngine.Calculation;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

public class RuleTypeHandlerRegistryTests
{
    private static EvaluationEvent Event(decimal amount) => new() { EventType = "order.created", ContactKey = "c1", Amount = amount };

    // CR 2026-10-06 Phase 4 (D2): the handler no longer rounds — these tests asserted the
    // round-down that 1.3.CL item 2 put here. Rounding now happens once, in
    // WinnerSelector.ApplyRounding, to the wallet's decimals in the rule's or program's direction;
    // the same payouts (12, 12.8, 12.89, 12.897) are asserted end to end in
    // IntegrationTests/Engine/RoundingCr1006Tests.
    [Fact]
    public void SpendRuleHandler_computes_amount_times_factor_unrounded()
    {
        var handler = new SpendRuleHandler();
        handler.Compute(new RuleCalculation { Factor = 0.3m }, Event(42.99m)).Should().Be(12.897m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(9)]
    public void SpendRuleHandler_ignores_the_target_wallets_decimals(int decimals)
    {
        var handler = new SpendRuleHandler();
        handler.Compute(new RuleCalculation { Factor = 1m }, Event(1.123456m), decimals).Should().Be(1.123456m);
    }

    [Fact]
    public void Handlers_other_than_spend_ignore_target_decimals()
    {
        IRuleTypeHandler handler = new FixedBonusRuleHandler();
        handler.Compute(new RuleCalculation { FixedValue = 25.555m }, Event(0m), 0).Should().Be(25.555m);
    }

    [Fact]
    public void SpendRuleHandler_returns_zero_when_factor_missing()
    {
        var handler = new SpendRuleHandler();
        handler.Compute(new RuleCalculation { Factor = null }, Event(100m)).Should().Be(0);
    }

    [Fact]
    public void FixedBonusRuleHandler_returns_configured_amount()
    {
        var handler = new FixedBonusRuleHandler();
        handler.Compute(new RuleCalculation { FixedValue = 25m }, Event(0m)).Should().Be(25m);
    }

    [Fact]
    public void Registry_resolves_each_handler_by_its_RuleType()
    {
        var registry = new RuleTypeHandlerRegistry(new IRuleTypeHandler[]
        {
            new SpendRuleHandler(), new FixedBonusRuleHandler(), new RedemptionRuleHandler()
        });

        registry.Resolve(RuleTypes.SpendRule).Should().BeOfType<SpendRuleHandler>();
        registry.Resolve(RuleTypes.FixedBonusRule).Should().BeOfType<FixedBonusRuleHandler>();
        registry.Resolve(RuleTypes.RedemptionRule).Should().BeOfType<RedemptionRuleHandler>();
    }

    [Fact]
    public void Registry_falls_back_to_zero_delta_for_unknown_rule_type()
    {
        var registry = new RuleTypeHandlerRegistry(new IRuleTypeHandler[] { new SpendRuleHandler() });

        var handler = registry.Resolve("SomeUnknownRuleType");
        handler.Compute(new RuleCalculation { Factor = 5m, FixedValue = 5m }, Event(100m)).Should().Be(0);
    }
}
