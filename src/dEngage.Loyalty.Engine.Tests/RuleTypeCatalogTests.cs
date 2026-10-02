using FluentAssertions;
using dEngage.Loyalty.RuleEngine.Metadata;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// CR 2026-09-30 item 8: each burn trigger allows only its own rule type, and reward.purchase none.
public class RuleTypeCatalogTests
{
    [Theory]
    [InlineData(EventTypes.PointsTransfer, RuleTypes.TransferRule, true)]
    [InlineData(EventTypes.PointsTransfer, RuleTypes.RedemptionRule, false)] // allowed before the fix
    [InlineData(EventTypes.PointsRedeem, RuleTypes.RedemptionRule, true)]
    [InlineData(EventTypes.PointsRedeem, RuleTypes.TransferRule, false)]
    [InlineData(EventTypes.RewardPurchase, RuleTypes.RedemptionRule, false)]
    [InlineData(EventTypes.RewardPurchase, RuleTypes.TransferRule, false)]
    [InlineData(EventTypes.RewardPurchase, RuleTypes.SpendRule, false)]
    [InlineData(EventTypes.OrderCreated, RuleTypes.SpendRule, true)] // events outside the allowlist are unaffected
    [InlineData(EventTypes.OrderCreated, RuleTypes.RedemptionRule, false)]
    public void Burn_triggers_accept_only_their_own_rule_type(string eventType, string ruleType, bool expected)
    {
        RuleTypeCatalog.IsCompatible(EventTypes.Describe(eventType), ruleType).Should().Be(expected);
    }

    [Fact]
    public void Reward_purchase_has_no_compatible_rule_type()
    {
        RuleTypeCatalog.CompatibleRuleTypes(EventTypes.Describe(EventTypes.RewardPurchase)).Should().BeEmpty();
    }

    [Fact]
    public void Points_transfer_lists_only_transfer_rule()
    {
        RuleTypeCatalog.CompatibleRuleTypes(EventTypes.Describe(EventTypes.PointsTransfer))
            .Should().Equal(RuleTypes.TransferRule);
    }

    [Fact]
    public void Generic_event_types_stay_compatible_with_every_rule_type()
    {
        RuleTypeCatalog.IsCompatible(null, RuleTypes.SpendRule).Should().BeTrue();
        RuleTypeCatalog.IsCompatible(null, RuleTypes.RedemptionRule).Should().BeTrue();
    }
}
