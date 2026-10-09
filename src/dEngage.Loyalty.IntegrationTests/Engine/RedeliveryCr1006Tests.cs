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

// CR 2026-10-06 R15 (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md §7.12): an
// event that failed after its rules posted (a later step threw) stays Failed in the inbox, so when
// it is replayed from the dead-letter queue — or redelivered after a consumer crash before the ack
// — the engine runs again for the same event id. The postings were
// deduped by their keys, but the budget reservation and the Redis counters were recorded again —
// and a rule that "already paid" or hit its cap through the first delivery let the next
// exclusive rule pay a second award for the same event.
public sealed class RedeliveryCr1006Tests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _points;

    public RedeliveryCr1006Tests()
    {
        _programId = _harness.AddProgram();
        _points = _harness.AddAccountType(_programId, "POINTS", "Points");

        // The Redis per-customer counters, read back from the ledger as LimitCacheService
        // rebuilds them, so caps see what was really posted.
        _harness.LimitCache.Setup(c => c.GetTotalAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid ruleId, string contact, CancellationToken _) => Posted(ruleId, contact));
        _harness.LimitCache.Setup(c => c.GetDailyAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid ruleId, string contact, CancellationToken _) => Posted(ruleId, contact));
    }

    public void Dispose() => _harness.Dispose();

    // Summed in memory: EF's Sqlite provider can't aggregate decimals.
    private decimal Posted(Guid ruleId, string contact) =>
        _harness.Db.LedgerEntries.AsNoTracking()
            .Where(l => l.RuleId == ruleId && l.ContactKey == contact)
            .Select(l => l.Delta).ToList().Sum();

    private decimal BudgetUsed(Guid ruleId) =>
        _harness.Db.RuleLimitCounters.AsNoTracking()
            .Where(c => c.RuleId == ruleId && c.CounterType == "budget_total")
            .Select(c => c.Value).ToList().Sum();

    private CachedRule AddRule(string trigger, int priority, decimal fixedValue, bool stackable = false,
        RuleLimits? limits = null, RuleSettings? configuration = null)
    {
        var rule = new CachedRule
        {
            Id = Guid.NewGuid(), ProgramId = _programId, Name = $"bonus-{priority}", Type = RuleTypes.FixedBonusRule,
            Trigger = trigger, Calculation = new RuleCalculation { FixedValue = fixedValue },
            TargetAccountTypeId = _points, Priority = priority, Stackable = stackable, StackMode = RuleStackMode.Additive,
            Limits = limits, Configuration = configuration, Version = 1
        };
        _harness.AddRule(rule);
        return rule;
    }

    private Task ProcessAsync(string eventType, string eventId, string contact) =>
        _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, eventId, new EvaluationEvent
        {
            EventType = eventType, ContactKey = contact, Amount = 50m, OccurredAt = DateTime.UtcNow,
            Data = JsonSerializer.SerializeToElement(new { contact_key = contact, amount = "50" })
        }, CancellationToken.None);

    [Theory]
    [InlineData(EventTypes.Signup)]
    [InlineData(EventTypes.KycCompleted)]
    public async Task A_redelivered_once_per_customer_event_does_not_pay_the_next_exclusive_rule(string eventType)
    {
        var first = AddRule(eventType, 20, 100m);
        var next = AddRule(eventType, 10, 30m);

        await ProcessAsync(eventType, "evt-1", "c1");
        await ProcessAsync(eventType, "evt-1", "c1"); // redelivery

        _harness.GetBalance("c1", _points).Should().Be(100m);
        _harness.Db.LedgerEntries.AsNoTracking().Count(l => l.RuleId == next.Id).Should().Be(0);
        _harness.Db.LedgerEntries.AsNoTracking().Count(l => l.RuleId == first.Id).Should().Be(1);
    }

    [Fact]
    public async Task A_redelivered_event_does_not_let_the_next_exclusive_rule_pay_when_the_first_reached_its_cap()
    {
        AddRule(EventTypes.OrderCreated, 20, 100m,
            limits: new RuleLimits { PerCustomerTotal = 100m, OnBreach = "Skip" });
        var next = AddRule(EventTypes.OrderCreated, 10, 30m);

        await ProcessAsync(EventTypes.OrderCreated, "order-1", "c2");
        await ProcessAsync(EventTypes.OrderCreated, "order-1", "c2"); // redelivery

        _harness.GetBalance("c2", _points).Should().Be(100m);
        _harness.Db.LedgerEntries.AsNoTracking().Count(l => l.RuleId == next.Id).Should().Be(0);
    }

    [Fact]
    public async Task A_redelivered_event_reserves_the_rule_budget_once()
    {
        var rule = AddRule(EventTypes.OrderCreated, 10, 40m, limits: new RuleLimits { RuleBudgetTotal = 1000m, OnBreach = "Clamp" });

        await ProcessAsync(EventTypes.OrderCreated, "order-1", "c3");
        await ProcessAsync(EventTypes.OrderCreated, "order-1", "c3"); // redelivery

        BudgetUsed(rule.Id).Should().Be(40m);
        _harness.GetBalance("c3", _points).Should().Be(40m);
    }

    [Fact]
    public async Task A_redelivered_event_increments_the_per_customer_counters_once()
    {
        var rule = AddRule(EventTypes.OrderCreated, 10, 40m);

        await ProcessAsync(EventTypes.OrderCreated, "order-1", "c4");
        await ProcessAsync(EventTypes.OrderCreated, "order-1", "c4"); // redelivery

        _harness.LimitCache.Verify(c => c.IncrementAsync(RuleEngineTestHarness.TenantSlug, rule.Id, "c4", 40m), Times.Once);
    }

    [Fact]
    public async Task A_redelivered_event_holds_a_delayed_posting_once_and_reserves_its_budget_once()
    {
        var rule = AddRule(EventTypes.OrderCreated, 10, 40m,
            limits: new RuleLimits { RuleBudgetTotal = 1000m, OnBreach = "Clamp" },
            configuration: new RuleSettings { Posting = "Delayed", HoldDays = 7 });

        await ProcessAsync(EventTypes.OrderCreated, "order-1", "c5");
        await ProcessAsync(EventTypes.OrderCreated, "order-1", "c5"); // redelivery

        _harness.Db.HeldPostings.AsNoTracking().Count(h => h.RuleId == rule.Id).Should().Be(1);
        BudgetUsed(rule.Id).Should().Be(40m);
        _harness.LimitCache.Verify(c => c.IncrementAsync(RuleEngineTestHarness.TenantSlug, rule.Id, "c5", 40m), Times.Once);
    }

    // The guard is per event id: a new event for the same customer is a new award.
    [Fact]
    public async Task A_new_event_still_pays_reserves_and_counts_again()
    {
        var rule = AddRule(EventTypes.OrderCreated, 10, 40m, limits: new RuleLimits { RuleBudgetTotal = 1000m, OnBreach = "Clamp" });

        await ProcessAsync(EventTypes.OrderCreated, "order-1", "c6");
        await ProcessAsync(EventTypes.OrderCreated, "order-2", "c6");

        _harness.GetBalance("c6", _points).Should().Be(80m);
        BudgetUsed(rule.Id).Should().Be(80m);
        _harness.LimitCache.Verify(c => c.IncrementAsync(RuleEngineTestHarness.TenantSlug, rule.Id, "c6", 40m), Times.Exactly(2));
    }
}
