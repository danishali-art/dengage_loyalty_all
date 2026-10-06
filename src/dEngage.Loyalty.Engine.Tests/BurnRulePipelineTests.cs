using System.Text.Json;
using FluentAssertions;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Calculation;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// CR 2026-10-05 item 1: redeem / transfer rules are applied only by their event handlers. The
// generic engine must never post them — that would debit a second time.
public sealed class BurnRulePipelineTests
{
    [Theory]
    [InlineData(RuleTypes.RedemptionRule, false)]
    [InlineData(RuleTypes.TransferRule, false)]
    [InlineData(RuleTypes.ReversalRule, false)]
    [InlineData(RuleTypes.SpendRule, true)]
    [InlineData(RuleTypes.FixedBonusRule, true)]
    [InlineData(RuleTypes.ManualAdjustmentRule, true)]
    public void Only_earn_and_adjust_rules_use_the_winner_selector_pipeline(string ruleType, bool expected) =>
        RuleTypes.UsesWinnerSelectorPipeline(ruleType).Should().Be(expected);

    [Fact]
    public async Task The_winner_selector_never_applies_a_redemption_rule()
    {
        var registry = new RuleTypeHandlerRegistry(new IRuleTypeHandler[] { new RedemptionRuleHandler() });
        var sut = new WinnerSelector(registry, new Mock<ILimitCacheService>().Object, new Mock<IRuleLimitEvaluator>().Object,
            NullLogger<WinnerSelector>.Instance);
        var rule = new CachedRule
        {
            Id = Guid.NewGuid(), Name = "redeem", Type = RuleTypes.RedemptionRule, Trigger = EventTypes.PointsRedeem,
            Calculation = new RuleCalculation { Factor = 0.01m, CashAccountTypeId = Guid.NewGuid() },
            TargetAccountTypeId = Guid.NewGuid(), Priority = 10
        };
        // Even with the amount readable, which was the latent double-debit (CR §2.1).
        var evt = new EvaluationEvent { EventType = EventTypes.PointsRedeem, ContactKey = "c1", Amount = 300m };

        var applied = await sut.SelectAsync("t1", [rule], evt, ConditionContext.Empty, CancellationToken.None);

        applied.Should().BeEmpty();
    }

    private static EventEnvelope Envelope(string eventType, object data) => new()
    {
        EventId = "e1",
        EventType = eventType,
        Tenant = "t1",
        OccurredAt = DateTime.UtcNow,
        Data = JsonSerializer.SerializeToElement(data)
    };

    // Conditions and min_event_amount on a burn rule must see the real burn amount.
    [Theory]
    [InlineData(EventTypes.PointsRedeem)]
    [InlineData(EventTypes.PointsTransfer)]
    public void A_burn_events_amount_is_its_points_amount(string eventType)
    {
        var evt = EvaluationEvent.FromEnvelope(Envelope(eventType, new { contact_key = "c1", points_amount = "300" }));

        evt.Amount.Should().Be(300m);
    }

    [Fact]
    public void A_burn_event_without_points_amount_falls_back_to_amount()
    {
        var evt = EvaluationEvent.FromEnvelope(Envelope(EventTypes.RewardPurchase, new { contact_key = "c1", amount = "12.5" }));

        evt.Amount.Should().Be(12.5m);
    }

    [Fact]
    public void An_earn_event_keeps_reading_amount_even_if_it_carries_points_amount()
    {
        var evt = EvaluationEvent.FromEnvelope(Envelope(EventTypes.OrderCreated, new { contact_key = "c1", amount = "100", points_amount = "999" }));

        evt.Amount.Should().Be(100m);
    }
}
