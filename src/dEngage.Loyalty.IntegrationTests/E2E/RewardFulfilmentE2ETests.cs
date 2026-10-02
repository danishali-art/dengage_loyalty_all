using System.Text.Json;
using dEngage.Loyalty.Consumer.Handlers;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.IntegrationTests.E2E;

// CR 2026-09-30 (A2/A4/O10, §3.5/§3.6): rewards are actually paid — cashback credits a CASH
// wallet, a tier upgrade moves the tier and can lock it against the nightly downgrade. Real
// Postgres: the purchase handler row-locks (FOR UPDATE) and TierDowngradeJob is raw SQL.
// Requires Docker. Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class RewardFulfilmentE2ETests : IAsyncLifetime
{
    private const string Tenant = "t1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private LoyaltyDbContext _db = default!;
    private Guid _tenantId;
    private Guid _programId;
    private Guid _pointsId;
    private Guid _cashId;
    private Guid _silverId;
    private Guid _goldId;
    private Guid _cashbackRewardId;
    private Guid _pendingRewardId;
    private Guid _tierRewardId;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<LoyaltyDbContext>().UseNpgsql(_container.GetConnectionString()).Options;
        _db = new LoyaltyDbContext(options);
        await _db.Database.MigrateAsync();

        _tenantId = Guid.NewGuid();
        _db.Tenants.Add(new Tenant { Id = _tenantId, Slug = Tenant, Name = Tenant, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });

        _programId = Guid.NewGuid();
        _db.Programs.Add(new ProgramEntity
        {
            Id = _programId, TenantId = _tenantId, Name = "Shop", Slug = "shop", Status = ProgramStatus.Active,
            PublicationStatus = ProgramPublicationStatus.Published, CreatedAt = DateTime.UtcNow
        });

        _pointsId = Guid.NewGuid();
        _cashId = Guid.NewGuid();
        _db.AccountTypes.AddRange(
            new AccountTypeEntity { Id = _pointsId, TenantId = _tenantId, ProgramId = _programId, Type = "POINTS", Name = "Points", Config = """{"decimals": 0}""", IsTierQualifying = true, CreatedAt = DateTime.UtcNow },
            new AccountTypeEntity { Id = _cashId, TenantId = _tenantId, ProgramId = _programId, Type = "CASH", Name = "Cash", Config = """{"currency": "SAR", "decimals": 2}""", CreatedAt = DateTime.UtcNow });

        _silverId = Guid.NewGuid();
        _goldId = Guid.NewGuid();
        _db.TierDefinitions.AddRange(
            new TierDefinition { Id = _silverId, TenantId = _tenantId, ProgramId = _programId, Name = "silver", DisplayName = "Silver", MinPoints = 0, QualifyingDays = 30, SortOrder = 1, CreatedAt = DateTime.UtcNow },
            new TierDefinition { Id = _goldId, TenantId = _tenantId, ProgramId = _programId, Name = "gold", DisplayName = "Gold", MinPoints = 10000, QualifyingDays = 30, SortOrder = 2, CreatedAt = DateTime.UtcNow });

        _cashbackRewardId = Guid.NewGuid();
        _pendingRewardId = Guid.NewGuid();
        _tierRewardId = Guid.NewGuid();
        _db.RewardDefinitions.AddRange(
            Reward(_cashbackRewardId, "shop_cash_10", RewardAcquisition.PointsPurchase, RewardType.Cashback, RewardStatus.Active,
                $$$"""{"amount": "10.00", "currency": "SAR", "cash_account_type_id": "{{{_cashId}}}"}""", pointsPrice: 100m),
            Reward(_pendingRewardId, "shop_cash_pending", RewardAcquisition.PointsPurchase, RewardType.Cashback, RewardStatus.PendingApproval,
                $$$"""{"amount": "5.00", "currency": "SAR", "cash_account_type_id": "{{{_cashId}}}"}""", pointsPrice: 50m),
            Reward(_tierRewardId, "shop_gold", RewardAcquisition.StreakCompletion, RewardType.TierUpgrade, RewardStatus.Active,
                $$$"""{"target_tier_id": "{{{_goldId}}}", "duration_days": 30}""", pointsPrice: null));
        await _db.SaveChangesAsync();

        // ledger_entries is list-partitioned by tenant slug (see PointsExpirationJobE2ETests).
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS ledger_entries_t1 PARTITION OF ledger_entries FOR VALUES IN ('t1')");
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    private RewardDefinition Reward(Guid id, string name, string acquisition, string type, string status, string typeConfig, decimal? pointsPrice) => new()
    {
        Id = id, TenantId = _tenantId, ProgramId = _programId, Name = name, DisplayName = name,
        Acquisition = acquisition, RewardType = type, Status = status, TypeConfig = typeConfig, IsActive = true,
        PointsPrice = pointsPrice, PointsAccountTypeId = pointsPrice is null ? null : _pointsId, CreatedAt = DateTime.UtcNow
    };

    private (RewardPurchaseHandler Handler, RewardFulfilmentService Fulfilment, LedgerService Ledger) Build()
    {
        var resolver = new TenantSlugResolver(_db, new TenantSlugCache());
        var ledger = new LedgerService(_db, resolver);
        var outbox = new OutboxService(_db, resolver);
        var fulfilment = new RewardFulfilmentService(_db, ledger, outbox, NullLogger<RewardFulfilmentService>.Instance);
        return (new RewardPurchaseHandler(ledger, outbox, fulfilment, _db, resolver), fulfilment, ledger);
    }

    private async Task SeedPointsAsync(string contactKey, decimal points)
    {
        var (_, _, ledger) = Build();
        var account = await ledger.UpsertAccountAsync(Tenant, contactKey, _pointsId);
        await ledger.AddEntryAsync(Tenant, account.Id, contactKey, points, LedgerReason.Earn,
            sourceEventId: $"seed-{contactKey}", idempotencyKey: $"seed-{contactKey}");
    }

    private static EventEnvelope Purchase(string eventId, string contactKey, Dictionary<string, string> reward) => new()
    {
        EventId = eventId,
        EventType = EventTypes.RewardPurchase,
        Tenant = Tenant,
        OccurredAt = DateTime.UtcNow,
        Data = JsonSerializer.SerializeToElement(new Dictionary<string, string>(reward) { ["contact_key"] = contactKey })
    };

    private decimal Balance(string contactKey, Guid accountTypeId) =>
        _db.CustomerAccounts.AsNoTracking()
            .Where(a => a.TenantId == _tenantId && a.ContactKey == contactKey && a.AccountTypeId == accountTypeId)
            .Select(a => a.Balance).SingleOrDefault();

    private OutboxEvent? Outbox(string dedupKey) =>
        _db.OutboxEvents.AsNoTracking().SingleOrDefault(o => o.TenantId == _tenantId && o.DedupKey == dedupKey);

    private CustomerAccount PointsAccount(string contactKey) =>
        _db.CustomerAccounts.AsNoTracking().Single(a => a.TenantId == _tenantId && a.ContactKey == contactKey && a.AccountTypeId == _pointsId);

    [Fact]
    public async Task Buying_a_cashback_reward_debits_points_credits_cash_once_and_reports_it()
    {
        await SeedPointsAsync("buyer", 500m);
        var (handler, _, _) = Build();
        var envelope = Purchase("e-buy", "buyer", new() { ["reward_name"] = "shop_cash_10" });

        await handler.HandleAsync(envelope, CancellationToken.None);
        await handler.HandleAsync(envelope, CancellationToken.None); // redelivery

        Balance("buyer", _pointsId).Should().Be(400m);
        Balance("buyer", _cashId).Should().Be(10.00m);
        _db.LedgerEntries.AsNoTracking().Count(l => l.TenantId == Tenant && l.Reason == LedgerReason.RewardCashback).Should().Be(1);
        var earned = Outbox("reward_earned:e-buy:" + _pointsId);
        earned.Should().NotBeNull();
        earned!.Payload.Should().Contain("cashback_amount").And.Contain(RewardFulfilmentOutcome.CashCredited);
    }

    [Fact]
    public async Task A_reward_can_be_bought_by_reward_id()
    {
        await SeedPointsAsync("by-id", 500m);
        var (handler, _, _) = Build();

        await handler.HandleAsync(Purchase("e-id", "by-id", new() { ["reward_id"] = _cashbackRewardId.ToString() }), CancellationToken.None);

        Balance("by-id", _cashId).Should().Be(10.00m);
    }

    [Fact]
    public async Task Cashback_is_refused_before_any_debit_when_the_program_is_not_live()
    {
        await SeedPointsAsync("paused", 500m);
        await _db.Programs.Where(p => p.Id == _programId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, ProgramStatus.Inactive));
        var (handler, _, _) = Build();

        await handler.HandleAsync(Purchase("e-paused", "paused", new() { ["reward_name"] = "shop_cash_10" }), CancellationToken.None);

        Balance("paused", _pointsId).Should().Be(500m);
        Balance("paused", _cashId).Should().Be(0m);
        Outbox("purchase_failed:e-paused")!.Payload.Should().Contain("program_not_live");
    }

    [Fact]
    public async Task A_cashback_reward_pending_approval_cannot_be_bought()
    {
        await SeedPointsAsync("early", 500m);
        var (handler, _, _) = Build();

        var act = () => handler.HandleAsync(Purchase("e-early", "early", new() { ["reward_name"] = "shop_cash_pending" }), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("reward_not_approved*");
        Balance("early", _pointsId).Should().Be(500m);
    }

    [Fact]
    public async Task A_tier_upgrade_reward_moves_the_tier_up_once_and_locks_it()
    {
        await SeedPointsAsync("climber", 10m);
        await _db.CustomerAccounts.Where(a => a.ContactKey == "climber").ExecuteUpdateAsync(s => s.SetProperty(a => a.TierId, _silverId));
        var (_, fulfilment, _) = Build();
        var reward = _db.RewardDefinitions.AsNoTracking().Single(r => r.Id == _tierRewardId);

        var first = await fulfilment.FulfilAsync(Tenant, _tenantId, reward, "climber", "e-streak", "streak:x:climber:1", CancellationToken.None);
        await _db.SaveChangesAsync();
        var again = await fulfilment.FulfilAsync(Tenant, _tenantId, reward, "climber", "e-streak", "streak:x:climber:1", CancellationToken.None);
        await _db.SaveChangesAsync();

        first.Outcome.Should().Be(RewardFulfilmentOutcome.TierUpgraded);
        again.Outcome.Should().Be(RewardFulfilmentOutcome.TierUpgraded);
        var account = PointsAccount("climber");
        account.TierId.Should().Be(_goldId);
        account.TierLockedUntil.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        _db.TierUpgradeLogs.AsNoTracking().Count(l => l.TenantId == _tenantId && l.ContactKey == "climber").Should().Be(1);
        Outbox("tier_changed:streak:x:climber:1:tier_upgrade")!.Payload.Should().Contain("\"reason\": \"reward\"");
    }

    [Fact]
    public async Task A_tier_upgrade_never_moves_a_customer_down()
    {
        await SeedPointsAsync("top", 10m);
        await _db.CustomerAccounts.Where(a => a.ContactKey == "top").ExecuteUpdateAsync(s => s.SetProperty(a => a.TierId, _goldId));
        var (_, fulfilment, _) = Build();
        var reward = _db.RewardDefinitions.AsNoTracking().Single(r => r.Id == _tierRewardId);

        var result = await fulfilment.FulfilAsync(Tenant, _tenantId, reward, "top", "e-top", "streak:x:top:1", CancellationToken.None);

        result.Outcome.Should().Be(RewardFulfilmentOutcome.AlreadyAtOrAbove);
        PointsAccount("top").TierLockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task The_nightly_downgrade_leaves_a_locked_tier_alone_until_the_lock_passes()
    {
        await SeedPointsAsync("locked", 10m);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Gold earned long ago, qualifying period over, only 10 points: would start grace tonight.
        await _db.CustomerAccounts.Where(a => a.ContactKey == "locked").ExecuteUpdateAsync(s => s
            .SetProperty(a => a.TierId, _goldId)
            .SetProperty(a => a.TierPeriodStart, today.AddDays(-60))
            .SetProperty(a => a.TierLockedUntil, today.AddDays(5)));
        var job = new TierDowngradeJob(_db, NullLogger<TierDowngradeJob>.Instance);

        await job.RunAsync();
        var whileLocked = PointsAccount("locked");
        whileLocked.TierId.Should().Be(_goldId, "the reward lock is still in force");
        whileLocked.TierExpiresAt.Should().BeNull("grace must not start while locked");

        // Lock passed: the normal path applies. With grace_days = 0 the job starts grace and
        // downgrades in the same run (10 points only qualify for Silver).
        await _db.CustomerAccounts.Where(a => a.ContactKey == "locked").ExecuteUpdateAsync(s => s.SetProperty(a => a.TierLockedUntil, today.AddDays(-1)));
        await job.RunAsync();
        PointsAccount("locked").TierId.Should().Be(_silverId, "once the lock has passed, the normal downgrade applies");
    }
}
