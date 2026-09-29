using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Engine;

// Tiqmo TQ06 — 3 consecutive months with total remittance >= 1000 SAR completes the streak and
// grants a fixed +25 CASH bonus; a below-threshold month neither advances nor breaks the streak
// (the month simply isn't "met" yet — it can still be topped up before month-end).
public sealed class StreakMonthlySumTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LoyaltyDbContext _db;
    private readonly Mock<ILedgerService> _ledger = new();
    private readonly Mock<IOutboxService> _outbox = new();
    private readonly StreakCampaignModule _sut;
    private readonly Guid _ruleId = Guid.NewGuid();
    private readonly Guid _cashAccountTypeId = Guid.NewGuid();
    private readonly Guid _tenantGuid = Guid.NewGuid();

    public StreakMonthlySumTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LoyaltyDbContext>().UseSqlite(_connection).Options;
        _db = new LoyaltyDbContext(options);
        _db.Database.EnsureCreated();
        _db.Tenants.Add(new Tenant { Id = _tenantGuid, Slug = "t1", Name = "t1", Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var tenantSlugResolver = new TenantSlugResolver(_db, new TenantSlugCache());
        _sut = new StreakCampaignModule(_db, _ledger.Object, _outbox.Object, tenantSlugResolver, NullLogger<StreakCampaignModule>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // 3-month streak, sum(amount)>=1000/month, UTC, fixed_bonus of 25, restarts after completion.
    private CachedCampaignConfig Config() => new()
    {
        Id = _ruleId,
        Name = "Monthly Remittance Streak",
        CampaignType = CampaignTypes.Streak,
        Trigger = "remittance",
        TargetAccountTypeId = _cashAccountTypeId,
        Streak = new StreakConfig
        {
            Period = StreakPeriod.Month,
            WeekStart = StreakWeekStart.Monday,
            TargetPeriods = 3,
            Aggregate = new StreakAggregate { Metric = StreakMetric.Sum, Threshold = 1000m },
            Timezone = "UTC",
            OnComplete = StreakOnComplete.Restart,
            Reward = new StreakReward { Kind = StreakRewardKind.FixedBonus, Amount = 25m }
        }
    };

    private static EvaluationEvent Remittance(decimal amount, DateTime occurredAt) => new()
    {
        EventType = "remittance",
        ContactKey = "tq_h",
        Amount = amount,
        OccurredAt = occurredAt
    };

    private Task<CampaignOutcome> Evaluate(string eventId, decimal amount, DateTime occurredAt) =>
        _sut.EvaluateAsync(new CampaignEvaluationRequest("t1", eventId, Config(), Remittance(amount, occurredAt)), CancellationToken.None);

    private Task<int> ProgressCountAsync() =>
        _db.StreakProgresses
            .Where(p => p.TenantId == _tenantGuid && p.CampaignId == _ruleId && p.ContactKey == "tq_h")
            .Select(p => (int?)p.StreakCount)
            .FirstOrDefaultAsync()
            .ContinueWith(t => t.Result ?? 0);

    [Fact]
    public async Task Two_transactions_crossing_the_monthly_threshold_advance_the_streak_by_one()
    {
        var monthAnchor = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        await Evaluate("evt-1", 600m, monthAnchor.AddMonths(-2));
        await Evaluate("evt-2", 500m, monthAnchor.AddMonths(-2).AddDays(3));

        (await ProgressCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_below_threshold_month_does_not_advance_the_counter()
    {
        var monthAnchor = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        await Evaluate("evt-1", 600m, monthAnchor.AddMonths(-2));
        await Evaluate("evt-2", 500m, monthAnchor.AddMonths(-2).AddDays(3));
        await Evaluate("evt-3", 1200m, monthAnchor.AddMonths(-1));
        await Evaluate("evt-4", 800m, monthAnchor);

        (await ProgressCountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Third_consecutive_month_completes_the_streak_and_grants_the_fixed_bonus()
    {
        var monthAnchor = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var account = new CustomerAccount { Id = Guid.NewGuid(), TenantId = _tenantGuid, ContactKey = "tq_h", AccountTypeId = _cashAccountTypeId };
        _ledger.Setup(l => l.UpsertAccountAsync("t1", "tq_h", _cashAccountTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        _ledger.Setup(l => l.AddEntryAsync(
                "t1", account.Id, "tq_h", 25m, LedgerReason.Earn, "evt-5",
                It.IsAny<string>(), _ruleId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LedgerEntry { Id = Guid.NewGuid() });

        await Evaluate("evt-1", 600m, monthAnchor.AddMonths(-2));
        await Evaluate("evt-2", 500m, monthAnchor.AddMonths(-2).AddDays(3));
        await Evaluate("evt-3", 1200m, monthAnchor.AddMonths(-1));
        await Evaluate("evt-4", 800m, monthAnchor);
        var outcome = await Evaluate("evt-5", 300m, monthAnchor.AddDays(1));

        outcome.Earned.Should().BeTrue();

        var log = await _db.StreakLogs.SingleAsync(l => l.TenantId == _tenantGuid && l.CampaignId == _ruleId);
        log.CompletionNo.Should().Be(1);

        _ledger.Verify(l => l.AddEntryAsync(
            "t1", account.Id, "tq_h", 25m, LedgerReason.Earn, "evt-5",
            It.IsAny<string>(), _ruleId, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(o => o.Enqueue("t1", dEngage.Loyalty.Shared.Events.OutboundEventTypes.StreakCompleted,
            "tq_h", It.IsAny<object>(), $"streak_completed:{_ruleId}:tq_h:1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
