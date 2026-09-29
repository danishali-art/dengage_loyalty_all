using FluentAssertions;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Calculation;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

public sealed class WinnerSelectorTests
{
    private readonly Mock<ILimitCacheService> _limitCache = new();
    private readonly Mock<IRuleLimitEvaluator> _limitEvaluator = new(); // CR-07 — unused unless a test sets the new Limits fields
    private readonly WinnerSelector _sut;
    private readonly Guid _accountTypeId = Guid.NewGuid();

    public WinnerSelectorTests()
    {
        var registry = new RuleTypeHandlerRegistry(new IRuleTypeHandler[] { new FixedBonusRuleHandler() });
        _sut = new WinnerSelector(registry, _limitCache.Object, _limitEvaluator.Object, NullLogger<WinnerSelector>.Instance);
    }

    private CachedRule Rule(string name, int priority, decimal delta, bool stackable = false, RuleLimits? limits = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = RuleTypes.FixedBonusRule,
        Trigger = "order.created",
        Calculation = new RuleCalculation { FixedValue = delta },
        TargetAccountTypeId = _accountTypeId,
        Priority = priority,
        Stackable = stackable,
        Limits = limits
    };

    private static EvaluationEvent Event() => new() { EventType = "order.created", ContactKey = "c1", Amount = 100m };

    [Fact]
    public async Task Highest_priority_non_stackable_rule_wins_and_lower_priority_ones_are_excluded()
    {
        var low = Rule("low", priority: 1, delta: 5m);
        var high = Rule("high", priority: 10, delta: 8m);

        var applied = await _sut.SelectAsync("t1", [low, high], Event(), ConditionContext.Empty, CancellationToken.None);

        applied.Should().ContainSingle();
        applied[0].Rule.Name.Should().Be("high");
        applied[0].Delta.Should().Be(8m);
    }

    [Fact]
    public async Task Stackable_rules_all_apply_alongside_the_non_stackable_winner()
    {
        var winner = Rule("winner", priority: 10, delta: 8m);
        var stackable1 = Rule("bonus1", priority: 1, delta: 2m, stackable: true);
        var stackable2 = Rule("bonus2", priority: 2, delta: 3m, stackable: true);

        var applied = await _sut.SelectAsync("t1", [winner, stackable1, stackable2], Event(), ConditionContext.Empty, CancellationToken.None);

        applied.Should().HaveCount(3);
        applied.Sum(a => a.Delta).Should().Be(13m);
    }

    [Fact]
    public async Task A_rule_whose_per_customer_total_limit_is_already_exhausted_cannot_win()
    {
        var exhausted = Rule("exhausted", priority: 10, delta: 8m, limits: new RuleLimits { PerCustomerTotal = 50m });
        var fallback = Rule("fallback", priority: 1, delta: 3m);

        _limitCache.Setup(x => x.GetTotalAsync("t1", exhausted.Id, "c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(50m); // already at the cap

        var applied = await _sut.SelectAsync("t1", [exhausted, fallback], Event(), ConditionContext.Empty, CancellationToken.None);

        applied.Should().ContainSingle();
        applied[0].Rule.Name.Should().Be("fallback");
    }

    [Fact]
    public async Task Stackable_rule_delta_is_clipped_to_the_remaining_per_customer_total_limit()
    {
        var rule = Rule("clipped", priority: 1, delta: 10m, stackable: true, limits: new RuleLimits { PerCustomerTotal = 50m });

        _limitCache.Setup(x => x.GetTotalAsync("t1", rule.Id, "c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(45m); // only 5 remaining out of a 10-delta rule

        var applied = await _sut.SelectAsync("t1", [rule], Event(), ConditionContext.Empty, CancellationToken.None);

        applied.Should().ContainSingle();
        applied[0].Delta.Should().Be(5m);
    }

    [Fact]
    public async Task A_rule_that_computes_zero_delta_is_excluded()
    {
        var zeroDelta = Rule("zero", priority: 1, delta: 0m);

        var applied = await _sut.SelectAsync("t1", [zeroDelta], Event(), ConditionContext.Empty, CancellationToken.None);

        applied.Should().BeEmpty();
    }

    [Fact]
    public async Task Different_account_types_are_evaluated_independently_and_can_each_produce_a_winner()
    {
        var otherAccountType = Guid.NewGuid();
        var ruleA = Rule("A", priority: 1, delta: 5m);
        var ruleB = Rule("B", priority: 1, delta: 7m);
        ruleB.TargetAccountTypeId = otherAccountType;

        var applied = await _sut.SelectAsync("t1", [ruleA, ruleB], Event(), ConditionContext.Empty, CancellationToken.None);

        applied.Should().HaveCount(2);
        applied.Should().Contain(a => a.Rule.Name == "A" && a.AccountTypeId == _accountTypeId);
        applied.Should().Contain(a => a.Rule.Name == "B" && a.AccountTypeId == otherAccountType);
    }
}
