using System.Text.Json;
using dEngage.Loyalty.Consumer;
using dEngage.Loyalty.Consumer.Handlers;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.IntegrationTests.E2E;

// CR 2026-10-06 (rule configuration & limits by trigger), Phase 0: confirm or rule out two refund
// gaps found by reading the code (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md
// §2.4 H1 and §2.8). The tests assert the CORRECT behaviour, so a failure confirms the gap; they
// become the regression tests for the Phase 1 fix.
// RefundService and ReversalRuleProcessor use Postgres-only SQL (metadata->>…), so this needs a
// real Postgres. Requires Docker. Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class RefundPathsCr1006E2ETests : IAsyncLifetime
{
    private const string Tenant = "t1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly BurnRuleFixture _rules = new();
    private LoyaltyDbContext _db = default!;
    private Guid _tenantId;
    private Guid _programId;
    private Guid _pointsId;

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
            Id = _programId, TenantId = _tenantId, Name = "p1", Status = ProgramStatus.Active,
            PublicationStatus = ProgramPublicationStatus.Published, CreatedAt = DateTime.UtcNow
        });

        _pointsId = Guid.NewGuid();
        _db.AccountTypes.Add(new AccountTypeEntity
        {
            Id = _pointsId, TenantId = _tenantId, ProgramId = _programId, Type = "POINTS", Name = "Points",
            Config = "{}", CreatedAt = DateTime.UtcNow
        });
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

    private TenantSlugResolver Resolver() => new(_db, new TenantSlugCache());

    private LedgerService Ledger() => new(_db, Resolver());

    private OrderRefundedHandler RefundHandler()
    {
        var resolver = Resolver();
        return new OrderRefundedHandler(new RefundService(_db, new LedgerService(_db, resolver), new OutboxService(_db, resolver), resolver));
    }

    // What RuleEngine.ProcessEventAsync does with the matched Reversal rules of an event.
    private ReversalRuleProcessor ReversalProcessor()
    {
        var resolver = Resolver();
        var limitEvaluator = new RuleLimitEvaluator(_db);
        return new ReversalRuleProcessor(
            _db, new LedgerService(_db, resolver), new OutboxService(_db, resolver),
            new RuleFireAuditWriter(_db, resolver),
            new BudgetReservationService(_db, resolver, limitEvaluator),
            NullLogger<ReversalRuleProcessor>.Instance);
    }

    private Task<CachedRule> AddEarnRuleAsync(RuleSettings? configuration = null) => _rules.AddAsync(_db, _tenantId, new CachedRule
    {
        Id = Guid.NewGuid(),
        ProgramId = _programId,
        Name = $"earn-{Guid.NewGuid():N}",
        Type = RuleTypes.FixedBonusRule,
        Trigger = EventTypes.OrderCreated,
        TargetAccountTypeId = _pointsId,
        Calculation = new RuleCalculation { FixedValue = 100m },
        Configuration = configuration,
        Priority = 10,
        Version = 1
    });

    private Task<CachedRule> AddReversalRuleAsync() => _rules.AddAsync(_db, _tenantId, new CachedRule
    {
        Id = Guid.NewGuid(),
        ProgramId = _programId,
        Name = $"reversal-{Guid.NewGuid():N}",
        Type = RuleTypes.ReversalRule,
        Trigger = EventTypes.OrderRefunded,
        TargetAccountTypeId = null,
        Calculation = new RuleCalculation { Mode = "full", AllowNegative = "clamp to zero" },
        Priority = 10,
        Version = 1
    });

    private async Task EarnAsync(string contactKey, string orderEventId, decimal points, Guid? ruleId)
    {
        var ledger = Ledger();
        var account = await ledger.UpsertAccountAsync(Tenant, contactKey, _pointsId);
        await ledger.AddEntryAsync(Tenant, account.Id, contactKey, points, LedgerReason.Earn,
            sourceEventId: orderEventId, idempotencyKey: $"{orderEventId}:{ruleId}", ruleId: ruleId);
    }

    private static EventEnvelope Refund(string eventId, string originalEventId, string? contactKey)
    {
        var data = new Dictionary<string, string>
        {
            ["original_event_id"] = originalEventId,
            ["refund_ratio"] = "1"
        };
        if (contactKey is not null) data["contact_key"] = contactKey;
        return new EventEnvelope
        {
            EventId = eventId,
            EventType = EventTypes.OrderRefunded,
            Tenant = Tenant,
            OccurredAt = DateTime.UtcNow,
            Data = JsonSerializer.SerializeToElement(data)
        };
    }

    private decimal Balance(string contactKey) =>
        _db.CustomerAccounts.AsNoTracking()
            .Where(a => a.TenantId == _tenantId && a.ContactKey == contactKey && a.AccountTypeId == _pointsId)
            .Select(a => a.Balance).SingleOrDefault();

    // §2.8, precondition: the worker runs the rule engine for order.refunded after the built-in
    // refund handler, but CampaignEvaluationService skips any event without a contact_key — and
    // contact_key is optional on order.refunded (OrderRefundedRequest.ContactKey is nullable).
    [Fact]
    public async Task Order_refunded_reaches_the_rule_engine_only_when_it_carries_a_contact_key()
    {
        var engine = new Mock<IRuleEngine>();
        var service = new CampaignEvaluationService(engine.Object, _db, Resolver());

        await service.EvaluateAsync(Refund("r-anon", "order-x", contactKey: null), CancellationToken.None);
        engine.Verify(e => e.ProcessEventAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<EvaluationEvent>(), It.IsAny<CancellationToken>()), Times.Never);

        await service.EvaluateAsync(Refund("r-known", "order-x", contactKey: "c1"), CancellationToken.None);
        engine.Verify(e => e.ProcessEventAsync(Tenant, _programId, "r-known",
            It.IsAny<EvaluationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // §2.8: a full refund of an order, sent with a contact_key, in a program with an active
    // Reversal rule on order.refunded. The customer holds other points, so the reversal's
    // clamp-to-zero default can't hide a second reversal.
    [Fact]
    public async Task A_full_refund_reverses_the_order_earn_only_once_when_a_reversal_rule_is_active()
    {
        var earnRule = await AddEarnRuleAsync();
        var reversalRule = await AddReversalRuleAsync();
        await EarnAsync("c1", "order-0", 500m, earnRule.Id);
        await EarnAsync("c1", "order-1", 100m, earnRule.Id);
        Balance("c1").Should().Be(600m);

        var refund = Refund("refund-1", "order-1", contactKey: "c1");

        // Same order as EventConsumerWorker: the event's handler first, then the rule engine,
        // which hands the matched Reversal rules to ReversalRuleProcessor.
        await RefundHandler().HandleAsync(refund, CancellationToken.None);
        await ReversalProcessor().ProcessAsync(Tenant, refund.EventId, new[] { reversalRule },
            EvaluationEvent.FromEnvelope(refund), ConditionContext.Empty, CancellationToken.None);

        var reversals = _db.LedgerEntries.AsNoTracking()
            .Where(l => l.TenantId == Tenant && l.SourceEventId == "refund-1")
            .Select(l => new { l.Reason, l.Delta })
            .ToList();

        using (new AssertionScope())
        {
            Balance("c1").Should().Be(500m, "the 100 points earned by order-1 are taken back once");
            reversals.Sum(r => r.Delta).Should().Be(-100m, "one refund must not reverse the same earn twice");
        }
    }

    // §2.4 H1: a refund arrives while the order's points are still held by a Delayed rule.
    [Fact]
    public async Task A_refund_during_the_hold_cancels_the_held_points()
    {
        var rule = await AddEarnRuleAsync(new RuleSettings { Posting = "Delayed", HoldDays = 14 });
        var account = await Ledger().UpsertAccountAsync(Tenant, "c2", _pointsId);

        // What LedgerPoster writes for a Delayed rule: a held posting, no ledger entry yet.
        _db.HeldPostings.Add(new HeldPosting
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            RuleId = rule.Id,
            CustomerAccountId = account.Id,
            ContactKey = "c2",
            Delta = 100m,
            Reason = LedgerReason.Earn,
            SourceEventId = "order-2",
            IdempotencyKey = $"order-2:{rule.Id}",
            HoldUntil = DateTime.UtcNow.AddDays(14),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var refundError = await Record.ExceptionAsync(() =>
            RefundHandler().HandleAsync(Refund("refund-2", "order-2", contactKey: "c2"), CancellationToken.None));

        await ReleaseDueAsync("order-2");

        using (new AssertionScope())
        {
            refundError.Should().BeNull("a refund of held points is a normal case, not a dead-lettered failure");
            Balance("c2").Should().Be(0m, "points held for a refunded order must never be posted");
        }
    }

    private Mock<ITierEvaluationService> TierEval { get; } = new();

    // The hold of the order's held postings ends, and the promotion job runs.
    private async Task<int> ReleaseDueAsync(string orderEventId)
    {
        await _db.HeldPostings.Where(h => h.SourceEventId == orderEventId)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.HoldUntil, DateTime.UtcNow.AddMinutes(-1)));
        _db.ChangeTracker.Clear();
        var resolver = Resolver();
        return await new DelayedPostingPromotionJob(_db, new LedgerService(_db, resolver), new OutboxService(_db, resolver),
                TierEval.Object, NullLogger<DelayedPostingPromotionJob>.Instance)
            .RunAsync(CancellationToken.None);
    }

    private async Task<HeldPosting> HoldAsync(CachedRule rule, string contactKey, string orderEventId, decimal delta)
    {
        var account = await Ledger().UpsertAccountAsync(Tenant, contactKey, _pointsId);
        var held = new HeldPosting
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            RuleId = rule.Id,
            CustomerAccountId = account.Id,
            ContactKey = contactKey,
            Delta = delta,
            Reason = LedgerReason.Earn,
            SourceEventId = orderEventId,
            IdempotencyKey = $"{orderEventId}:{rule.Id}",
            HoldUntil = DateTime.UtcNow.AddDays(14),
            CreatedAt = DateTime.UtcNow
        };
        _db.HeldPostings.Add(held);
        await _db.SaveChangesAsync();
        return held;
    }

    private static EventEnvelope PartialRefund(string eventId, string originalEventId, string contactKey) => new()
    {
        EventId = eventId,
        EventType = EventTypes.OrderRefunded,
        Tenant = Tenant,
        OccurredAt = DateTime.UtcNow,
        Data = JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            ["original_event_id"] = originalEventId,
            ["refund_ratio"] = "0.5",
            ["contact_key"] = contactKey
        })
    };

    // H1: a partial refund during the hold takes back its share once — a redelivered refund event
    // takes nothing more — and the rest is posted when the hold ends.
    [Fact]
    public async Task A_partial_refund_during_the_hold_takes_back_its_share_once_and_the_rest_is_posted()
    {
        var rule = await AddEarnRuleAsync(new RuleSettings { Posting = "Delayed", HoldDays = 14 });
        var held = await HoldAsync(rule, "c3", "order-3", 100m);

        var refund = PartialRefund("refund-3", "order-3", "c3");
        await RefundHandler().HandleAsync(refund, CancellationToken.None);
        _db.ChangeTracker.Clear();
        await RefundHandler().HandleAsync(refund, CancellationToken.None); // redelivery

        _db.HeldPostingRefunds.AsNoTracking().Where(r => r.HeldPostingId == held.Id).Select(r => r.Delta)
            .ToList().Should().Equal(50m);
        _db.HeldPostings.AsNoTracking().Single(h => h.Id == held.Id).CancelledAt.Should().BeNull("half is still owed");

        (await ReleaseDueAsync("order-3")).Should().Be(1);
        Balance("c3").Should().Be(50m);
        (await ReleaseDueAsync("order-3")).Should().Be(0, "a posted row is never posted again");
        Balance("c3").Should().Be(50m);
    }

    // R15 (§7.12): a redelivered partial reversal redid the reversal (half of the earn was still
    // unreversed, so the cap let it): the ledger key deduped the entry, but the budget was given
    // back again and points.reversed was enqueued again, which violated the outbox dedupe index —
    // the redelivery always failed, so the event could never complete.
    [Fact]
    public async Task A_redelivered_partial_reversal_gives_the_budget_back_once()
    {
        var earnRule = await AddEarnRuleAsync();
        var reversalRule = await _rules.AddAsync(_db, _tenantId, new CachedRule
        {
            Id = Guid.NewGuid(), ProgramId = _programId, Name = $"reversal-{Guid.NewGuid():N}",
            Type = RuleTypes.ReversalRule, Trigger = "tenant.order_cancelled", TargetAccountTypeId = null,
            Calculation = new RuleCalculation { Mode = "proportional", AllowNegative = "clamp to zero" },
            Priority = 10, Version = 1
        });
        await EarnAsync("c5", "order-5", 100m, earnRule.Id);
        var budget = new BudgetReservationService(_db, Resolver(), new RuleLimitEvaluator(_db));
        (await budget.LockAndGetUsageAsync(Tenant, earnRule.Id, BudgetReservationService.BudgetTotal, "", null, CancellationToken.None))
            .Should().Be(100m, "the counter is seeded from the earn");

        var cancel = PartialRefund("cancel-5", "order-5", "c5");
        for (var delivery = 0; delivery < 2; delivery++)
        {
            await ReversalProcessor().ProcessAsync(Tenant, cancel.EventId, new[] { reversalRule },
                EvaluationEvent.FromEnvelope(cancel), ConditionContext.Empty, CancellationToken.None);
            _db.ChangeTracker.Clear();
        }

        Balance("c5").Should().Be(50m);
        _db.LedgerEntries.AsNoTracking().Count(l => l.Reason == LedgerReason.RuleReversal && l.SourceEventId == "cancel-5")
            .Should().Be(1);
        _db.RuleLimitCounters.AsNoTracking()
            .Single(c => c.RuleId == earnRule.Id && c.CounterType == BudgetReservationService.BudgetTotal)
            .Value.Should().Be(50m, "half of the 100 earned was given back, once");
    }

    // H1: a full refund cancels the held posting, and its redelivery still succeeds.
    [Fact]
    public async Task A_redelivered_full_refund_of_cancelled_held_points_still_succeeds()
    {
        var rule = await AddEarnRuleAsync(new RuleSettings { Posting = "Delayed", HoldDays = 14 });
        var held = await HoldAsync(rule, "c4", "order-4", 100m);
        var refund = Refund("refund-4", "order-4", contactKey: "c4");

        await RefundHandler().HandleAsync(refund, CancellationToken.None);
        _db.ChangeTracker.Clear();
        var redelivery = () => RefundHandler().HandleAsync(refund, CancellationToken.None);

        await redelivery.Should().NotThrowAsync();
        _db.HeldPostings.AsNoTracking().Single(h => h.Id == held.Id).CancelledAt.Should().NotBeNull();
        (await ReleaseDueAsync("order-4")).Should().Be(0);
        Balance("c4").Should().Be(0m);
    }

    // H2 + H3: a released posting is announced like an immediate award, and the tier is
    // re-evaluated when the wallet is the program's tier-qualifying one.
    [Fact]
    public async Task A_released_posting_is_announced_and_reevaluates_the_tier()
    {
        await _db.AccountTypes.Where(a => a.Id == _pointsId).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsTierQualifying, true));
        var settings = new RuleSettings { Posting = "Delayed", HoldDays = 14, NotifyOnAward = true };
        var rule = await AddEarnRuleAsync(settings);
        // BurnRuleFixture stores no configuration; the job reads Notify on award from the rule row.
        var configuration = JsonSerializer.Serialize(settings);
        await _db.Rules.Where(r => r.Id == rule.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Configuration, configuration));
        var held = await HoldAsync(rule, "c5", "order-5", 100m);

        (await ReleaseDueAsync("order-5")).Should().Be(1);
        (await ReleaseDueAsync("order-5")).Should().Be(0);

        Balance("c5").Should().Be(100m);
        var outbox = _db.OutboxEvents.AsNoTracking().Where(o => o.TenantId == _tenantId).ToList();
        outbox.Where(o => o.DedupKey == $"points_earned:order-5:held:{held.Id}").Should().ContainSingle()
            .Which.EventType.Should().Be(OutboundEventTypes.PointsEarned);
        outbox.Where(o => o.DedupKey == $"rule_awarded:order-5:{rule.Id}").Should().ContainSingle()
            .Which.EventType.Should().Be(OutboundEventTypes.RuleAwarded);
        TierEval.Verify(t => t.EvaluateAsync(Tenant, "c5", _programId, _pointsId, "order-5", It.IsAny<CancellationToken>()), Times.Once);
    }
}
