using FluentAssertions;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// Uses SQLite in-memory — StreakCampaignModule opens its own transaction and does raw-SQL
// idempotency inserts against streak_applied_event, neither of which the EF InMemory provider supports.
public sealed class StreakCampaignModuleTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LoyaltyDbContext _db;
    private readonly Mock<ILedgerService> _ledger = new();
    private readonly Mock<IOutboxService> _outbox = new();
    private readonly StreakCampaignModule _sut;
    private readonly Guid _ruleId = Guid.NewGuid();
    private readonly Guid _accountTypeId = Guid.NewGuid();
    private readonly Guid _tenantGuid = Guid.NewGuid();

    public StreakCampaignModuleTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LoyaltyDbContext>().UseSqlite(_connection).Options;
        _db = new LoyaltyDbContext(options);
        _db.Database.EnsureCreated();
        _db.Tenants.Add(new Tenant { Id = _tenantGuid, Slug = "t1", Name = "t1", Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var tenantSlugResolver = new TenantSlugResolver(_db, new TenantSlugCache());
        _sut = new StreakCampaignModule(_db, _ledger.Object, _outbox.Object, tenantSlugResolver,
            new Mock<dEngage.Loyalty.RuleEngine.Processing.IRewardFulfilmentService>().Object, NullLogger<StreakCampaignModule>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // 2-day streak, count>=1 per day, UTC, fixed_bonus of 5, restarts after completion.
    private CachedCampaignConfig Config(int targetPeriods = 2) => new()
    {
        Id = _ruleId,
        Name = "2-day streak",
        CampaignType = CampaignTypes.Streak,
        Trigger = "card.transaction",
        TargetAccountTypeId = _accountTypeId,
        Streak = new StreakConfig
        {
            Period = StreakPeriod.Day,
            WeekStart = StreakWeekStart.Monday,
            TargetPeriods = targetPeriods,
            Aggregate = new StreakAggregate { Metric = StreakMetric.Count, Threshold = 1 },
            Timezone = "UTC",
            OnComplete = StreakOnComplete.Restart,
            Reward = new StreakReward { Kind = StreakRewardKind.FixedBonus, Amount = 5m }
        }
    };

    private static EvaluationEvent Event(DateTime occurredAt) => new()
    {
        EventType = "card.transaction",
        ContactKey = "c1",
        Amount = 10m,
        OccurredAt = occurredAt
    };

    private Task<CampaignOutcome> Evaluate(string eventId, DateTime occurredAt, CachedCampaignConfig? config = null) =>
        _sut.EvaluateAsync(new CampaignEvaluationRequest("t1", eventId, config ?? Config(), Event(occurredAt)), CancellationToken.None);

    [Fact]
    public async Task First_met_period_advances_the_streak_to_one_without_completing()
    {
        var outcome = await Evaluate("evt-1", new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        outcome.Earned.Should().BeFalse();
        var progress = await _db.StreakProgresses.SingleAsync(p => p.TenantId == _tenantGuid && p.CampaignId == _ruleId && p.ContactKey == "c1");
        progress.StreakCount.Should().Be(1);
        progress.Completions.Should().Be(0);
        _ledger.Verify(l => l.AddEntryAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<DateTime?>()),
            Times.Never);
    }

    [Fact]
    public async Task Consecutive_days_met_completes_the_streak_and_grants_the_fixed_bonus()
    {
        var account = new CustomerAccount { Id = Guid.NewGuid(), TenantId = _tenantGuid, ContactKey = "c1", AccountTypeId = _accountTypeId };
        _ledger.Setup(l => l.UpsertAccountAsync("t1", "c1", _accountTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        _ledger.Setup(l => l.AddEntryAsync(
                "t1", account.Id, "c1", 5m, LedgerReason.Earn, "evt-2",
                It.IsAny<string>(), _ruleId, It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(new LedgerEntry { Id = Guid.NewGuid() });

        await Evaluate("evt-1", new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var outcome = await Evaluate("evt-2", new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc));

        outcome.Earned.Should().BeTrue();

        var progress = await _db.StreakProgresses.SingleAsync(p => p.TenantId == _tenantGuid && p.CampaignId == _ruleId && p.ContactKey == "c1");
        progress.Completions.Should().Be(1);
        progress.StreakCount.Should().Be(0); // on_complete: restart

        var log = await _db.StreakLogs.SingleAsync(l => l.TenantId == _tenantGuid && l.CampaignId == _ruleId);
        log.CompletionNo.Should().Be(1);
        log.RewardKind.Should().Be(StreakRewardKind.FixedBonus);

        _ledger.Verify(l => l.AddEntryAsync(
            "t1", account.Id, "c1", 5m, LedgerReason.Earn, "evt-2",
            It.IsAny<string>(), _ruleId, It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<DateTime?>()), Times.Once);
        _outbox.Verify(o => o.Enqueue("t1", dEngage.Loyalty.Shared.Events.OutboundEventTypes.StreakCompleted,
            "c1", It.IsAny<object>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_gap_day_resets_the_streak_to_one_instead_of_continuing_the_count()
    {
        await Evaluate("evt-1", new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        // skip Jan 2 entirely — Jan 3 is not consecutive with Jan 1
        await Evaluate("evt-2", new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc));

        var progress = await _db.StreakProgresses.SingleAsync(p => p.TenantId == _tenantGuid && p.CampaignId == _ruleId && p.ContactKey == "c1");
        progress.StreakCount.Should().Be(1); // reset, not 2
    }

    [Fact]
    public async Task Replaying_the_same_event_id_is_idempotent_and_does_not_advance_the_streak_twice()
    {
        var first = await Evaluate("evt-1", new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var replay = await Evaluate("evt-1", new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        replay.Earned.Should().BeFalse();
        var progress = await _db.StreakProgresses.SingleAsync(p => p.TenantId == _tenantGuid && p.CampaignId == _ruleId && p.ContactKey == "c1");
        progress.StreakCount.Should().Be(1); // still 1, not advanced by the replay
    }

    [Fact]
    public async Task Multiple_events_within_the_same_day_count_toward_one_period_not_several()
    {
        await Evaluate("evt-1", new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        await Evaluate("evt-2", new DateTime(2026, 1, 1, 15, 0, 0, DateTimeKind.Utc));

        var progress = await _db.StreakProgresses.SingleAsync(p => p.TenantId == _tenantGuid && p.CampaignId == _ruleId && p.ContactKey == "c1");
        progress.StreakCount.Should().Be(1); // same day → one period, still just the first completion

        var state = await _db.StreakPeriodStates.SingleAsync(s => s.TenantId == _tenantGuid && s.CampaignId == _ruleId);
        state.AggCount.Should().Be(2); // both events did accumulate into the same period's state
    }
}
