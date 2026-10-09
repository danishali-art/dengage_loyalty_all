using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Cache;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// CR-08 (docs/scope-change-rules A8): rule configuration — test mode, notify-on-award, and
// delayed posting (held state + promotion job), exercised against the real Sqlite-backed harness.
public sealed class RuleConfigurationTests : IDisposable
{
    private readonly RuleEngineTestHarness _harness = new();
    private readonly Guid _programId;
    private readonly Guid _pointsAccountTypeId;

    public RuleConfigurationTests()
    {
        _programId = _harness.AddProgram();
        _pointsAccountTypeId = _harness.AddAccountType(_programId, "POINTS", "Points");

        _harness.LimitCache.Setup(c => c.GetTotalAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        _harness.LimitCache.Setup(c => c.GetDailyAsync(RuleEngineTestHarness.TenantSlug, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
    }

    public void Dispose() => _harness.Dispose();

    private static EvaluationEvent CardEvent(string contact, decimal amount) => new()
    {
        EventType = "card.transaction",
        ContactKey = contact,
        Amount = amount,
        OccurredAt = DateTime.UtcNow,
        Data = System.Text.Json.JsonSerializer.SerializeToElement(new { contact_key = contact, amount = amount.ToString("F2") })
    };

    // CR 2026-10-06 D20: test mode was removed from every rule (stakeholder decision) — this test
    // used to assert that a test-mode rule posts nothing. A stored flag is now ignored: the rule
    // pays like any other. (Earn rules that had it on were disabled by migration, D21.)
    [Fact]
    public async Task A_stored_test_mode_flag_is_ignored_and_the_rule_posts()
    {
        var ruleId = Guid.NewGuid();
        _harness.AddRule(new CachedRule
        {
            Id = ruleId, ProgramId = _programId, Name = "Test Mode Rule",
            Type = RuleTypes.FixedBonusRule, Trigger = "card.transaction",
            Calculation = new RuleCalculation { FixedValue = 10m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100, Stackable = false, ExclusivityGroup = "g1",
            Configuration = new RuleSettings { TestMode = true },
            Version = 1
        });

        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("c1", 100m), CancellationToken.None);

        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(10m, "test mode no longer exists — the flag is ignored");
        _harness.Db.RuleFireAudits.Any(a => a.RuleId == ruleId && a.SourceEventId == "evt-1" && a.LedgerEntryId != null).Should().BeTrue();
        _harness.OutboxHasDedupKey("points_earned:evt-1").Should().BeTrue();
    }

    [Fact]
    public async Task Notify_on_award_enqueues_a_distinct_outbox_event()
    {
        var ruleId = Guid.NewGuid();
        _harness.AddRule(new CachedRule
        {
            Id = ruleId, ProgramId = _programId, Name = "Notify Rule",
            Type = RuleTypes.FixedBonusRule, Trigger = "card.transaction",
            Calculation = new RuleCalculation { FixedValue = 10m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100, Stackable = false, ExclusivityGroup = "g1",
            Configuration = new RuleSettings { NotifyOnAward = true },
            Version = 1
        });

        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("c1", 100m), CancellationToken.None);

        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(10m);
        _harness.OutboxHasDedupKey($"rule_awarded:evt-1:{ruleId}").Should().BeTrue();
    }

    [Fact]
    public async Task Delayed_posting_holds_then_the_promotion_job_posts_once_due()
    {
        var ruleId = Guid.NewGuid();
        _harness.AddRule(new CachedRule
        {
            Id = ruleId, ProgramId = _programId, Name = "Delayed Rule",
            Type = RuleTypes.FixedBonusRule, Trigger = "card.transaction",
            Calculation = new RuleCalculation { FixedValue = 10m },
            TargetAccountTypeId = _pointsAccountTypeId,
            Priority = 100, Stackable = false, ExclusivityGroup = "g1",
            Configuration = new RuleSettings { Posting = "Delayed", HoldDays = 3 },
            Version = 1
        });

        await _harness.Engine.ProcessEventAsync(RuleEngineTestHarness.TenantSlug, _programId, "evt-1", CardEvent("c1", 100m), CancellationToken.None);

        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(0m, "still held, not yet due");
        _harness.Db.HeldPostings.Count(h => h.RuleId == ruleId && h.PostedAt == null).Should().Be(1);

        // Not due yet — promotion job is a no-op.
        var resolver = new dEngage.Loyalty.Schema.TenantSlugResolver(_harness.Db, new dEngage.Loyalty.Schema.TenantSlugCache());
        var job = new DelayedPostingPromotionJob(_harness.Db, new dEngage.Loyalty.Ledger.LedgerService(_harness.Db, resolver),
            new dEngage.Loyalty.Ledger.OutboxService(_harness.Db, resolver), Mock.Of<dEngage.Loyalty.RuleEngine.ITierEvaluationService>(),
            NullLogger<DelayedPostingPromotionJob>.Instance);
        (await job.RunAsync(CancellationToken.None)).Should().Be(0);
        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(0m);

        // Backdate HoldUntil to simulate the hold period elapsing.
        var held = _harness.Db.HeldPostings.Single(h => h.RuleId == ruleId);
        held.HoldUntil = DateTime.UtcNow.AddMinutes(-1);
        _harness.Db.SaveChanges();

        (await job.RunAsync(CancellationToken.None)).Should().Be(1);
        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(10m, "promotion job posted the held delta");
        _harness.Db.HeldPostings.Single(h => h.RuleId == ruleId).PostedAt.Should().NotBeNull();

        // Idempotent — a second run posts nothing more.
        (await job.RunAsync(CancellationToken.None)).Should().Be(0);
        _harness.GetBalance("c1", _pointsAccountTypeId).Should().Be(10m);
    }
}
