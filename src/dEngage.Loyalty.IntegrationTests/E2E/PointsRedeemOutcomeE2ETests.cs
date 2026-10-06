using System.Text.Json;
using dEngage.Loyalty.Consumer.Handlers;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.IntegrationTests.E2E;

// CR 2026-09-30 §3.9 step 4: points.redeem reports its outcome like points.transfer —
// redeem_failed for a business failure (processed, not dead-lettered), redeemed on success.
// CR 2026-10-05: the redeem always runs on a RedemptionRule (the wallet's own `redemption`
// settings are never read), only in a live program.
// PointsRedeemHandler takes a row lock (SELECT ... FOR UPDATE), which Sqlite can't run, so this
// needs a real Postgres. Requires Docker. Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class PointsRedeemOutcomeE2ETests : IAsyncLifetime
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
    private Guid _cashId;
    private Guid _cash3dpId;
    private Guid _otherPointsId;

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

        _cashId = Guid.NewGuid();
        _cash3dpId = Guid.NewGuid();
        _pointsId = Guid.NewGuid();
        _otherPointsId = Guid.NewGuid();
        _db.AccountTypes.AddRange(
            new AccountTypeEntity
            {
                Id = _cashId, TenantId = _tenantId, ProgramId = _programId, Type = "CASH", Name = "Cash",
                Config = """{"currency": "SAR", "decimals": 2}""", CreatedAt = DateTime.UtcNow
            },
            new AccountTypeEntity
            {
                Id = _cash3dpId, TenantId = _tenantId, ProgramId = _programId, Type = "CASH", Name = "Cash KWD",
                Config = """{"currency": "KWD", "decimals": 3}""", CreatedAt = DateTime.UtcNow
            },
            // The wallet's own redemption settings differ from every rule below on purpose:
            // they only pre-fill new rules in the portal, and must never decide a payout.
            new AccountTypeEntity
            {
                Id = _pointsId, TenantId = _tenantId, ProgramId = _programId, Type = "POINTS", Name = "Points",
                Config = $$$"""{"redemption": {"target_account_type_id": "{{{_cashId}}}", "rate": 0.5, "min_points": 1}}""",
                CreatedAt = DateTime.UtcNow
            },
            new AccountTypeEntity
            {
                Id = _otherPointsId, TenantId = _tenantId, ProgramId = _programId, Type = "POINTS", Name = "Other points",
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

    private (PointsRedeemHandler Handler, LedgerService Ledger) Build()
    {
        var resolver = new TenantSlugResolver(_db, new TenantSlugCache());
        var ledger = new LedgerService(_db, resolver);
        return (new PointsRedeemHandler(ledger, new OutboxService(_db, resolver), _db, resolver, _rules.Resolver(_db, resolver)), ledger);
    }

    private Task<CachedRule> AddRuleAsync(CachedRule rule) => _rules.AddAsync(_db, _tenantId, rule);

    private CachedRule Rule(decimal rate = 0.01m, decimal? min = 100m, int priority = 10, Guid? cashId = null,
        RuleLimits? limits = null, ConditionTree? conditions = null) =>
        BurnRuleFixture.Redemption(_programId, _pointsId, cashId ?? _cashId, rate, min, priority, limits, conditions);

    private async Task SeedPointsAsync(string contactKey, decimal points)
    {
        var (_, ledger) = Build();
        var account = await ledger.UpsertAccountAsync(Tenant, contactKey, _pointsId);
        await ledger.AddEntryAsync(Tenant, account.Id, contactKey, points, LedgerReason.Earn,
            sourceEventId: $"seed-{contactKey}", idempotencyKey: $"seed-{contactKey}");
    }

    private static EventEnvelope Redeem(string eventId, string contactKey, string points, Guid sourceAccountTypeId) => new()
    {
        EventId = eventId,
        EventType = EventTypes.PointsRedeem,
        Tenant = Tenant,
        OccurredAt = DateTime.UtcNow,
        Data = JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            ["contact_key"] = contactKey,
            ["points_amount"] = points,
            ["source_account_type_id"] = sourceAccountTypeId.ToString()
        })
    };

    private decimal Balance(string contactKey, Guid accountTypeId) =>
        _db.CustomerAccounts.AsNoTracking()
            .Where(a => a.TenantId == _tenantId && a.ContactKey == contactKey && a.AccountTypeId == accountTypeId)
            .Select(a => a.Balance).SingleOrDefault();

    private OutboxEvent? Outbox(string dedupKey) =>
        _db.OutboxEvents.AsNoTracking().SingleOrDefault(o => o.TenantId == _tenantId && o.DedupKey == dedupKey);

    private async Task SetProgramAsync(string status, string publication)
    {
        await _db.Programs.Where(p => p.Id == _programId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status).SetProperty(p => p.PublicationStatus, publication));
    }

    [Fact]
    public async Task Successful_redeem_uses_the_rule_and_never_the_wallet_settings()
    {
        var rule = await AddRuleAsync(Rule(rate: 0.01m));
        await SeedPointsAsync("ok", 500m);
        var (handler, _) = Build();

        await handler.HandleAsync(Redeem("e-ok", "ok", "300", _pointsId), CancellationToken.None);

        Balance("ok", _pointsId).Should().Be(200m);
        Balance("ok", _cashId).Should().Be(3.00m); // 300 × the rule's 0.01, not the wallet's 0.5
        var redeemed = Outbox("points_redeemed:e-ok");
        redeemed.Should().NotBeNull();
        redeemed!.EventType.Should().Be(OutboundEventTypes.PointsRedeemed);
        redeemed.Payload.Should().Contain(rule.Id.ToString());
        Outbox("redeem_failed:e-ok").Should().BeNull();

        // Only the points debit carries the rule (budgets count rows by rule); the audit row
        // records the rule version.
        _db.LedgerEntries.AsNoTracking().Where(l => l.TenantId == Tenant && l.SourceEventId == "e-ok")
            .Select(l => l.RuleId).ToList().Should().BeEquivalentTo(new Guid?[] { rule.Id, null });
        _db.RuleFireAudits.AsNoTracking().Single(a => a.SourceEventId == "e-ok").RuleVersion.Should().Be(1);
    }

    [Fact]
    public async Task Without_a_rule_for_the_wallet_the_redeem_fails_with_no_rule()
    {
        await AddRuleAsync(BurnRuleFixture.Redemption(_programId, _otherPointsId, _cashId, 0.01m));
        await SeedPointsAsync("norule", 500m);
        var (handler, _) = Build();

        var act = () => handler.HandleAsync(Redeem("e-norule", "norule", "300", _pointsId), CancellationToken.None);

        await act.Should().NotThrowAsync();
        Balance("norule", _pointsId).Should().Be(500m);
        Balance("norule", _cashId).Should().Be(0m);
        Outbox("redeem_failed:e-norule")!.Payload.Should().Contain("no_rule");
    }

    [Fact]
    public async Task A_rule_applies_only_when_its_conditions_hold()
    {
        // points_amount >= 1000 — this event redeems 300.
        var conditions = new ConditionTree
        {
            Op = "AND",
            Groups = { new ConditionGroup { Op = "AND", Conditions = { new ConditionLeaf { Field = "points_amount", Operator = "gte", Value = new ConditionValue { Type = "number", Data = JsonSerializer.SerializeToElement(1000) } } } } }
        };
        await AddRuleAsync(Rule(conditions: conditions));
        await SeedPointsAsync("cond", 1500m);
        var (handler, _) = Build();

        await handler.HandleAsync(Redeem("e-cond", "cond", "300", _pointsId), CancellationToken.None);
        await handler.HandleAsync(Redeem("e-cond-ok", "cond", "1000", _pointsId), CancellationToken.None);

        Outbox("redeem_failed:e-cond")!.Payload.Should().Contain("no_rule");
        Outbox("points_redeemed:e-cond-ok").Should().NotBeNull(); // the same rule applies once its condition holds
        Balance("cond", _pointsId).Should().Be(500m);
    }

    [Fact]
    public async Task The_highest_priority_rule_wins()
    {
        await AddRuleAsync(Rule(rate: 0.01m, priority: 1));
        await AddRuleAsync(Rule(rate: 0.02m, priority: 50));
        await SeedPointsAsync("prio", 500m);
        var (handler, _) = Build();

        await handler.HandleAsync(Redeem("e-prio", "prio", "300", _pointsId), CancellationToken.None);

        Balance("prio", _cashId).Should().Be(6.00m);
    }

    // R-O12: a failed minimum on the winning rule never falls through to a looser rule.
    [Fact]
    public async Task Below_the_winning_rules_minimum_fails_without_trying_a_lower_priority_rule()
    {
        await AddRuleAsync(Rule(min: 1000m, priority: 50));
        await AddRuleAsync(Rule(min: null, priority: 1));
        await SeedPointsAsync("tiny", 500m);
        var (handler, _) = Build();

        var act = () => handler.HandleAsync(Redeem("e-tiny", "tiny", "300", _pointsId), CancellationToken.None);

        await act.Should().NotThrowAsync();
        Balance("tiny", _pointsId).Should().Be(500m);
        Outbox("redeem_failed:e-tiny")!.Payload.Should().Contain("below_minimum");
    }

    [Fact]
    public async Task A_rule_without_a_minimum_accepts_any_amount()
    {
        await AddRuleAsync(Rule(min: null));
        await SeedPointsAsync("anymin", 500m);
        var (handler, _) = Build();

        await handler.HandleAsync(Redeem("e-anymin", "anymin", "1", _pointsId), CancellationToken.None);

        Balance("anymin", _pointsId).Should().Be(499m);
    }

    [Fact]
    public async Task Insufficient_points_is_reported_with_redeem_failed_instead_of_throwing()
    {
        await AddRuleAsync(Rule());
        await SeedPointsAsync("short", 150m);
        var (handler, _) = Build();

        var act = () => handler.HandleAsync(Redeem("e-short", "short", "200", _pointsId), CancellationToken.None);

        await act.Should().NotThrowAsync();
        Balance("short", _pointsId).Should().Be(150m);
        Balance("short", _cashId).Should().Be(0m);
        var failed = Outbox("redeem_failed:e-short");
        failed.Should().NotBeNull();
        failed!.EventType.Should().Be(OutboundEventTypes.PointsRedeemFailed);
        failed.Payload.Should().Contain("insufficient_points");
    }

    [Fact]
    public async Task A_rule_budget_that_cannot_take_the_whole_amount_refuses_the_redeem()
    {
        await AddRuleAsync(Rule(limits: new RuleLimits { RuleBudgetTotal = 250m }));
        await SeedPointsAsync("budget", 500m);
        var (handler, _) = Build();

        await handler.HandleAsync(Redeem("e-budget", "budget", "300", _pointsId), CancellationToken.None);

        Balance("budget", _pointsId).Should().Be(500m);
        Outbox("redeem_failed:e-budget")!.Payload.Should().Contain("rule_limit_reached");
    }

    [Theory]
    [InlineData(ProgramStatus.Inactive, ProgramPublicationStatus.Published)]
    [InlineData(ProgramStatus.Active, ProgramPublicationStatus.Draft)]
    public async Task A_program_that_is_not_live_refuses_the_redeem(string status, string publication)
    {
        await AddRuleAsync(Rule());
        await SeedPointsAsync("paused", 500m);
        await SetProgramAsync(status, publication);
        var (handler, _) = Build();

        await handler.HandleAsync(Redeem("e-paused", "paused", "300", _pointsId), CancellationToken.None);

        Balance("paused", _pointsId).Should().Be(500m);
        Outbox("redeem_failed:e-paused")!.Payload.Should().Contain("program_not_live");
    }

    [Fact]
    public async Task Cash_is_rounded_down_to_the_cash_wallets_decimals()
    {
        await AddRuleAsync(Rule(rate: 0.01999m, min: null));                     // 300 → 5.997 → 5.99 at 2 dp
        await AddRuleAsync(BurnRuleFixture.Redemption(_programId, _otherPointsId, _cash3dpId, 0.012345m)); // 300 → 3.7035 → 3.703
        await SeedPointsAsync("round", 500m);
        var (handler, ledger) = Build();
        var other = await ledger.UpsertAccountAsync(Tenant, "round", _otherPointsId);
        await ledger.AddEntryAsync(Tenant, other.Id, "round", 500m, LedgerReason.Earn, sourceEventId: "seed-round-2", idempotencyKey: "seed-round-2");

        await handler.HandleAsync(Redeem("e-round2", "round", "300", _pointsId), CancellationToken.None);
        await handler.HandleAsync(Redeem("e-round3", "round", "300", _otherPointsId), CancellationToken.None);

        Balance("round", _cashId).Should().Be(5.99m);
        Balance("round", _cash3dpId).Should().Be(3.703m);
    }

    // Regression guard for the redelivery check added with the failure event: a redelivered
    // event (crash after commit, before the inbox update) sees the already-debited balance and,
    // without the check, would announce a false redeem_failed — also after the program was paused.
    [Fact]
    public async Task Redelivered_successful_redeem_posts_once_and_never_reports_a_failure()
    {
        await AddRuleAsync(Rule());
        await SeedPointsAsync("again", 500m);
        var (handler, _) = Build();
        var envelope = Redeem("e-again", "again", "300", _pointsId);

        await handler.HandleAsync(envelope, CancellationToken.None);
        await SetProgramAsync(ProgramStatus.Inactive, ProgramPublicationStatus.Published);
        await handler.HandleAsync(envelope, CancellationToken.None);

        Balance("again", _pointsId).Should().Be(200m);
        Balance("again", _cashId).Should().Be(3.00m);
        _db.LedgerEntries.AsNoTracking().Count(l => l.TenantId == Tenant && l.SourceEventId == "e-again").Should().Be(2); // debit + credit, once
        Outbox("redeem_failed:e-again").Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_points_wallet_still_fails_the_event()
    {
        var (handler, _) = Build();

        var act = () => handler.HandleAsync(Redeem("e-cfg", "cfg", "100", Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("points_account_type_not_found*");
        Outbox("redeem_failed:e-cfg").Should().BeNull();
    }
}
