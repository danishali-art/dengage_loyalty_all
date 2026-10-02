using FluentAssertions;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Campaigns;
using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// CR 2026-09-30 (A2): a streak that pays a reward definition now hands it to
// IRewardFulfilmentService (cashback credit / tier upgrade) instead of only announcing it, and
// only an approved reward is granted. Same Sqlite setup as StreakCampaignModuleTests.
public sealed class StreakRewardDefinitionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LoyaltyDbContext _db;
    private readonly Mock<ILedgerService> _ledger = new();
    private readonly Mock<IOutboxService> _outbox = new();
    private readonly Mock<IRewardFulfilmentService> _fulfilment = new();
    private readonly StreakCampaignModule _sut;
    private readonly Guid _campaignId = Guid.NewGuid();
    private readonly Guid _tenantGuid = Guid.NewGuid();
    private readonly Guid _programId = Guid.NewGuid();
    private readonly Guid _rewardId = Guid.NewGuid();

    public StreakRewardDefinitionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LoyaltyDbContext>().UseSqlite(_connection).Options;
        _db = new LoyaltyDbContext(options);
        _db.Database.EnsureCreated();
        _db.Tenants.Add(new Tenant { Id = _tenantGuid, Slug = "t1", Name = "t1", Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
        _db.Programs.Add(new Schema.Entities.Program { Id = _programId, TenantId = _tenantGuid, Name = "p", Slug = "p1", Status = ProgramStatus.Active, CreatedAt = DateTime.UtcNow });
        _db.SaveChanges();

        var tenantSlugResolver = new TenantSlugResolver(_db, new TenantSlugCache());
        _sut = new StreakCampaignModule(_db, _ledger.Object, _outbox.Object, tenantSlugResolver, _fulfilment.Object,
            NullLogger<StreakCampaignModule>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void AddReward(string status)
    {
        _db.RewardDefinitions.Add(new RewardDefinition
        {
            Id = _rewardId, TenantId = _tenantGuid, ProgramId = _programId,
            Name = "p1_cash_25", DisplayName = "Cash 25",
            Acquisition = RewardAcquisition.StreakCompletion, RewardType = RewardType.Cashback,
            TypeConfig = "{}", IsActive = true, Status = status, CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
    }

    private CachedCampaignConfig Config() => new()
    {
        Id = _campaignId,
        Name = "2-day streak",
        CampaignType = CampaignTypes.Streak,
        Trigger = "card.transaction",
        TargetAccountTypeId = Guid.NewGuid(),
        Streak = new StreakConfig
        {
            Period = StreakPeriod.Day,
            WeekStart = StreakWeekStart.Monday,
            TargetPeriods = 2,
            Aggregate = new StreakAggregate { Metric = StreakMetric.Count, Threshold = 1 },
            Timezone = "UTC",
            OnComplete = StreakOnComplete.Restart,
            Reward = new StreakReward { Kind = StreakRewardKind.RewardDefinition, RewardDefinitionId = _rewardId }
        }
    };

    private Task<CampaignOutcome> Evaluate(string eventId, DateTime occurredAt) =>
        _sut.EvaluateAsync(new CampaignEvaluationRequest("t1", eventId, Config(), new EvaluationEvent
        {
            EventType = "card.transaction", ContactKey = "c1", Amount = 10m, OccurredAt = occurredAt
        }), CancellationToken.None);

    private async Task CompleteStreakAsync()
    {
        await Evaluate("evt-1", new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        await Evaluate("evt-2", new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Completion_pays_an_approved_reward_through_the_fulfilment_seam_and_enriches_reward_earned()
    {
        AddReward(RewardStatus.Active);
        _fulfilment.Setup(f => f.FulfilAsync("t1", _tenantGuid, It.Is<RewardDefinition>(r => r.Id == _rewardId), "c1", "evt-2",
                $"streak:{_campaignId}:c1:1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RewardFulfilment(RewardFulfilmentOutcome.CashCredited,
                new Dictionary<string, object?> { ["outcome"] = RewardFulfilmentOutcome.CashCredited, ["cashback_amount"] = "25.00" },
                "ledger-entry-1"));

        object? earnedPayload = null;
        _outbox.Setup(o => o.Enqueue("t1", OutboundEventTypes.RewardEarned, "c1", It.IsAny<object>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, object, string?, CancellationToken>((_, _, _, data, _, _) => earnedPayload = data)
            .ReturnsAsync(new OutboxEvent());

        await CompleteStreakAsync();

        _fulfilment.Verify(f => f.FulfilAsync("t1", _tenantGuid, It.IsAny<RewardDefinition>(), "c1", "evt-2",
            $"streak:{_campaignId}:c1:1", It.IsAny<CancellationToken>()), Times.Once);
        var payload = earnedPayload.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
        payload["reward_type"].Should().Be(RewardType.Cashback);
        payload["cashback_amount"].Should().Be("25.00");
        payload["outcome"].Should().Be(RewardFulfilmentOutcome.CashCredited);

        // §3.5: a cashback's ledger entry is the streak log's reward reference.
        (await _db.StreakLogs.SingleAsync(l => l.CampaignId == _campaignId)).RewardRef.Should().Be("ledger-entry-1");
    }

    [Fact]
    public async Task Completion_does_not_pay_a_reward_that_is_still_pending_approval()
    {
        AddReward(RewardStatus.PendingApproval);

        await CompleteStreakAsync();

        _fulfilment.Verify(f => f.FulfilAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<RewardDefinition>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _outbox.Verify(o => o.Enqueue("t1", OutboundEventTypes.RewardEarned, It.IsAny<string>(), It.IsAny<object>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
