using FluentAssertions;
using dEngage.Loyalty.RuleEngine.Calculation;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Shared;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

public class RuleTypeHandlerRegistryTests
{
    private static EvaluationEvent Event(decimal amount) => new() { EventType = "order.created", ContactKey = "c1", Amount = amount };

    [Fact]
    public void SpendRuleHandler_computes_floor_of_amount_times_factor()
    {
        var handler = new SpendRuleHandler();
        var result = handler.Compute(new RuleCalculation { Factor = 0.3m }, Event(42.99m));
        result.Should().Be(12m); // floor(42.99 * 0.3) = floor(12.897) = 12
    }

    // 1.3.CL item 2: the target wallet's decimals sets the precision, always rounding down.
    [Theory]
    [InlineData(0, "12")]
    [InlineData(1, "12.8")]
    [InlineData(2, "12.89")]
    [InlineData(3, "12.897")]
    [InlineData(4, "12.897")]
    public void SpendRuleHandler_floors_to_the_target_wallets_decimal_places(int decimals, string expected)
    {
        var handler = new SpendRuleHandler();
        var result = handler.Compute(new RuleCalculation { Factor = 0.3m }, Event(42.99m), decimals);
        result.Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SpendRuleHandler_clamps_decimals_to_the_ledgers_four_places()
    {
        var handler = new SpendRuleHandler();
        handler.Compute(new RuleCalculation { Factor = 1m }, Event(1.123456m), 9).Should().Be(1.1234m);
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
    public void StampRuleHandler_returns_one_stamp_for_positive_amount()
    {
        var handler = new StampRuleHandler();
        handler.Compute(new RuleCalculation(), Event(1m)).Should().Be(1m);
    }

    [Fact]
    public void StampRuleHandler_returns_zero_for_zero_amount_order()
    {
        var handler = new StampRuleHandler();
        handler.Compute(new RuleCalculation(), Event(0m)).Should().Be(0m);
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
            new SpendRuleHandler(), new StampRuleHandler(), new FixedBonusRuleHandler()
        });

        registry.Resolve(RuleTypes.SpendRule).Should().BeOfType<SpendRuleHandler>();
        registry.Resolve(RuleTypes.StampRule).Should().BeOfType<StampRuleHandler>();
        registry.Resolve(RuleTypes.FixedBonusRule).Should().BeOfType<FixedBonusRuleHandler>();
    }

    [Fact]
    public void Registry_falls_back_to_zero_delta_for_unknown_rule_type()
    {
        var registry = new RuleTypeHandlerRegistry(new IRuleTypeHandler[] { new SpendRuleHandler() });

        var handler = registry.Resolve("SomeUnknownRuleType");
        handler.Compute(new RuleCalculation { Factor = 5m, FixedValue = 5m }, Event(100m)).Should().Be(0);
    }
}
