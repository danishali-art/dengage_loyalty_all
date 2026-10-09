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

// CR 2026-10-06 Part 1b (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md):
// - D12: signup / kyc.completed pay each rule at most once per customer (§3.6).
// - D6/D13: every per-customer cap follows On breach on exclusive and stackable rules (§3.7).
public sealed class OnceOnlyAndCapsCr1006Tests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _points;

    public OnceOnlyAndCapsCr1006Tests()
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

    private CachedRule AddRule(string trigger, string type, int priority, bool stackable,
        decimal? factor = null, decimal? fixedValue = null, RuleLimits? limits = null, RuleSettings? configuration = null)
    {
        var rule = new CachedRule
        {
            Id = Guid.NewGuid(),
            ProgramId = _programId,
            Name = $"{type}-{priority}",
            Type = type,
            Trigger = trigger,
            Calculation = new RuleCalculation { Factor = factor, FixedValue = fixedValue },
            TargetAccountTypeId = _points,
            Priority = priority,
            Stackable = stackable,
            StackMode = RuleStackMode.Additive,
            Limits = limits,
            Configuration = configuration,
            Version = 1
        };
        _harness.AddRule(rule);
        return rule;
    }

    private Task ProcessAsync(string eventType, string eventId, string contact, decimal amount = 0m) =>
        _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, eventId, new EvaluationEvent
        {
            EventType = eventType,
            ContactKey = contact,
            Amount = amount,
            OccurredAt = DateTime.UtcNow,
            Data = JsonSerializer.SerializeToElement(new { contact_key = contact, amount = amount.ToString("F2") })
        }, CancellationToken.None);

    // ── D12: once per customer per rule ──

    [Theory]
    [InlineData(EventTypes.Signup)]
    [InlineData(EventTypes.KycCompleted)]
    public async Task A_repeated_once_per_customer_event_pays_each_rule_once(string eventType)
    {
        var rule = AddRule(eventType, RuleTypes.FixedBonusRule, 10, stackable: false, fixedValue: 100m);

        await ProcessAsync(eventType, "evt-1", "c1");
        await ProcessAsync(eventType, "evt-2", "c1"); // a resend with a new event id

        _harness.GetBalance("c1", _points).Should().Be(100m);
        _harness.Db.LedgerEntries.AsNoTracking().Single(l => l.RuleId == rule.Id)
            .IdempotencyKey.Should().Be($"once:{rule.Id}:c1");
        _harness.Db.RuleFireAudits.AsNoTracking().Count(a => a.RuleId == rule.Id).Should().Be(1, "a repeat leaves no audit row");
        _harness.OutboxHasDedupKey("points_earned:evt-2").Should().BeFalse("a repeat sends no points.earned");
    }

    [Fact]
    public async Task Two_rules_on_the_first_event_both_pay_and_neither_pays_again()
    {
        AddRule(EventTypes.Signup, RuleTypes.FixedBonusRule, 10, stackable: false, fixedValue: 100m);
        AddRule(EventTypes.Signup, RuleTypes.FixedBonusRule, 5, stackable: true, fixedValue: 10m);

        await ProcessAsync(EventTypes.Signup, "evt-1", "c2");
        await ProcessAsync(EventTypes.Signup, "evt-2", "c2");

        _harness.GetBalance("c2", _points).Should().Be(110m);
    }

    [Fact]
    public async Task A_rule_that_already_paid_lets_the_next_exclusive_rule_pay_once()
    {
        AddRule(EventTypes.KycCompleted, RuleTypes.FixedBonusRule, 20, stackable: false, fixedValue: 100m);
        await ProcessAsync(EventTypes.KycCompleted, "evt-1", "c3");

        // A new campaign rule added later is a new promise — it pays once (option A).
        AddRule(EventTypes.KycCompleted, RuleTypes.FixedBonusRule, 10, stackable: false, fixedValue: 30m);
        await ProcessAsync(EventTypes.KycCompleted, "evt-2", "c3");
        await ProcessAsync(EventTypes.KycCompleted, "evt-3", "c3");

        _harness.GetBalance("c3", _points).Should().Be(130m);
    }

    [Fact]
    public async Task An_award_made_before_the_change_counts_as_already_received()
    {
        var rule = AddRule(EventTypes.Signup, RuleTypes.FixedBonusRule, 10, stackable: false, fixedValue: 100m);
        // Posted under the old {eventId}:{ruleId} key, before once-only existed (D4a).
        var account = await new dEngage.Loyalty.Ledger.LedgerService(_harness.Db,
                new dEngage.Loyalty.Schema.TenantSlugResolver(_harness.Db, new dEngage.Loyalty.Schema.TenantSlugCache()))
            .UpsertAccountAsync(RuleEngineTestHarness.TenantSlug, "c4", _points);
        await new dEngage.Loyalty.Ledger.LedgerService(_harness.Db,
                new dEngage.Loyalty.Schema.TenantSlugResolver(_harness.Db, new dEngage.Loyalty.Schema.TenantSlugCache()))
            .AddEntryAsync(RuleEngineTestHarness.TenantSlug, account.Id, "c4", 100m, LedgerReason.Earn,
                "old-evt", $"old-evt:{rule.Id}", rule.Id);

        await ProcessAsync(EventTypes.Signup, "evt-new", "c4");

        _harness.GetBalance("c4", _points).Should().Be(100m);
    }

    [Fact]
    public async Task A_legacy_per_customer_total_can_not_allow_a_second_award()
    {
        AddRule(EventTypes.Signup, RuleTypes.FixedBonusRule, 10, stackable: false, fixedValue: 100m,
            limits: new RuleLimits { PerCustomerTotal = 1000m });

        await ProcessAsync(EventTypes.Signup, "evt-1", "c5");
        await ProcessAsync(EventTypes.Signup, "evt-2", "c5");

        _harness.GetBalance("c5", _points).Should().Be(100m);
    }

    [Fact]
    public async Task Events_that_are_not_once_per_customer_still_pay_every_time()
    {
        AddRule(EventTypes.CardTransaction, RuleTypes.FixedBonusRule, 10, stackable: false, fixedValue: 10m);

        await ProcessAsync(EventTypes.CardTransaction, "evt-1", "c6", 50m);
        await ProcessAsync(EventTypes.CardTransaction, "evt-2", "c6", 50m);

        _harness.GetBalance("c6", _points).Should().Be(20m);
    }

    // ── D6/D13: per-customer caps follow On breach on both kinds of rule ──

    // The worked example from the CR document (§3.7): A exclusive 10% capped at 250, B exclusive
    // flat 20, C stackable 50 capped at 120; each order is 1,000.
    [Fact]
    public async Task The_worked_example_never_exceeds_a_cap_and_lets_the_next_exclusive_rule_take_over()
    {
        AddRule(EventTypes.OrderCreated, RuleTypes.SpendRule, 10, stackable: false, factor: 0.1m,
            limits: new RuleLimits { PerCustomerTotal = 250m });
        AddRule(EventTypes.OrderCreated, RuleTypes.FixedBonusRule, 5, stackable: false, fixedValue: 20m);
        AddRule(EventTypes.OrderCreated, RuleTypes.FixedBonusRule, 1, stackable: true, fixedValue: 50m,
            limits: new RuleLimits { PerCustomerTotal = 120m });

        var balances = new List<decimal>();
        for (var i = 1; i <= 5; i++)
        {
            await ProcessAsync(EventTypes.OrderCreated, $"order-{i}", "c7", 1000m);
            balances.Add(_harness.GetBalance("c7", _points));
        }

        // Order 3: A pays 50 (exactly 250, not 300) and C 20; order 4 on: B takes over.
        balances.Should().Equal(150m, 300m, 370m, 390m, 410m);
    }

    [Fact]
    public async Task An_exclusive_rule_with_on_breach_skip_pays_nothing_past_its_cap_and_the_next_rule_wins()
    {
        AddRule(EventTypes.OrderCreated, RuleTypes.FixedBonusRule, 10, stackable: false, fixedValue: 100m,
            limits: new RuleLimits { PerCustomerTotal = 150m, OnBreach = "Skip" });
        AddRule(EventTypes.OrderCreated, RuleTypes.FixedBonusRule, 5, stackable: false, fixedValue: 20m);

        await ProcessAsync(EventTypes.OrderCreated, "o-1", "c8", 10m); // 100 (room 150)
        await ProcessAsync(EventTypes.OrderCreated, "o-2", "c8", 10m); // 100 doesn't fit 50 → skipped, B pays 20

        _harness.GetBalance("c8", _points).Should().Be(120m);
    }

    [Fact]
    public async Task A_stackable_rule_with_on_breach_skip_no_longer_trims_its_total_cap()
    {
        AddRule(EventTypes.OrderCreated, RuleTypes.FixedBonusRule, 1, stackable: true, fixedValue: 50m,
            limits: new RuleLimits { PerCustomerTotal = 120m, OnBreach = "Skip" });

        for (var i = 1; i <= 3; i++)
            await ProcessAsync(EventTypes.OrderCreated, $"s-{i}", "c9", 10m);

        _harness.GetBalance("c9", _points).Should().Be(100m, "the third 50 doesn't fit the 20 left and is skipped, not trimmed");
    }
}
