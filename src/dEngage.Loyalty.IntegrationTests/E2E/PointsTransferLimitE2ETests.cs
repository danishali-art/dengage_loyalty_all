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

// CR 2026-10-05: a transfer always runs on a TransferRule, whose daily transfer limit
// (`maxPerDay`) is what PointsTransferHandler enforces — the wallet's own `transfer.daily_limit`
// only pre-fills new rules and is never read. Only in a live program. Real Postgres — the handler
// row-locks both accounts (FOR UPDATE). Requires Docker. Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class PointsTransferLimitE2ETests : IAsyncLifetime
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

        _pointsId = Guid.NewGuid();
        _otherPointsId = Guid.NewGuid();
        _db.AccountTypes.AddRange(
            // The wallet's own limit (5000) differs from the rules' on purpose — it must not decide.
            new AccountTypeEntity { Id = _pointsId, TenantId = _tenantId, ProgramId = _programId, Type = "POINTS", Name = "Points", Config = """{"decimals": 0, "transfer": {"daily_limit": 5000}}""", CreatedAt = DateTime.UtcNow },
            new AccountTypeEntity { Id = _otherPointsId, TenantId = _tenantId, ProgramId = _programId, Type = "POINTS", Name = "Other", Config = """{"decimals": 0}""", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS ledger_entries_t1 PARTITION OF ledger_entries FOR VALUES IN ('t1')");
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    private (PointsTransferHandler Handler, LedgerService Ledger) Build()
    {
        var resolver = new TenantSlugResolver(_db, new TenantSlugCache());
        var ledger = new LedgerService(_db, resolver);
        return (new PointsTransferHandler(ledger, new OutboxService(_db, resolver), _db, resolver, _rules.Resolver(_db, resolver)), ledger);
    }

    private Task<CachedRule> AddRuleAsync(decimal maxPerDay, int priority = 10, RuleLimits? limits = null, Guid? wallet = null) =>
        _rules.AddAsync(_db, _tenantId, BurnRuleFixture.Transfer(_programId, wallet ?? _pointsId, maxPerDay, priority, limits));

    private async Task SeedAsync(string contactKey, Guid accountTypeId, decimal points)
    {
        var (_, ledger) = Build();
        var account = await ledger.UpsertAccountAsync(Tenant, contactKey, accountTypeId);
        await ledger.AddEntryAsync(Tenant, account.Id, contactKey, points, LedgerReason.Earn,
            sourceEventId: $"seed-{contactKey}-{accountTypeId}", idempotencyKey: $"seed-{contactKey}-{accountTypeId}");
    }

    private static EventEnvelope Transfer(string eventId, string from, string to, string points, Guid accountTypeId) => new()
    {
        EventId = eventId,
        EventType = EventTypes.PointsTransfer,
        Tenant = Tenant,
        OccurredAt = DateTime.UtcNow,
        Data = JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            ["contact_key"] = from,
            ["target_contact_key"] = to,
            ["points_amount"] = points,
            ["source_account_type_id"] = accountTypeId.ToString()
        })
    };

    private decimal Balance(string contactKey, Guid accountTypeId) =>
        _db.CustomerAccounts.AsNoTracking()
            .Where(a => a.TenantId == _tenantId && a.ContactKey == contactKey && a.AccountTypeId == accountTypeId)
            .Select(a => a.Balance).SingleOrDefault();

    private OutboxEvent? Outbox(string dedupKey) =>
        _db.OutboxEvents.AsNoTracking().SingleOrDefault(o => o.TenantId == _tenantId && o.DedupKey == dedupKey);

    [Fact]
    public async Task Transfers_up_to_the_rules_daily_limit_succeed_and_the_next_one_is_refused()
    {
        var rule = await AddRuleAsync(maxPerDay: 3000m);
        await SeedAsync("sender", _pointsId, 10000m);
        var (handler, _) = Build();

        await handler.HandleAsync(Transfer("t-1", "sender", "receiver", "1500", _pointsId), CancellationToken.None);
        await handler.HandleAsync(Transfer("t-2", "sender", "receiver", "1500", _pointsId), CancellationToken.None); // exactly 3000 today
        await handler.HandleAsync(Transfer("t-3", "sender", "receiver", "1", _pointsId), CancellationToken.None);    // over the rule's limit

        Balance("sender", _pointsId).Should().Be(7000m);
        Balance("receiver", _pointsId).Should().Be(3000m);
        var failed = Outbox("transfer_failed:t-3")!;
        failed.EventType.Should().Be(OutboundEventTypes.PointsTransferFailed);
        failed.Payload.Should().Contain("daily_limit_exceeded");
        Outbox("transfer:t-1")!.Payload.Should().Contain(rule.Id.ToString());

        // Only the sender's debit carries the rule.
        _db.LedgerEntries.AsNoTracking().Where(l => l.TenantId == Tenant && l.SourceEventId == "t-1")
            .Select(l => l.RuleId).ToList().Should().BeEquivalentTo(new Guid?[] { rule.Id, null });
    }

    [Fact]
    public async Task A_wallet_without_a_rule_refuses_transfers_with_no_rule()
    {
        await AddRuleAsync(maxPerDay: 3000m, wallet: _otherPointsId);
        await SeedAsync("sender2", _pointsId, 1000m);
        var (handler, _) = Build();

        var act = () => handler.HandleAsync(Transfer("t-x", "sender2", "receiver2", "100", _pointsId), CancellationToken.None);

        await act.Should().NotThrowAsync();
        Balance("sender2", _pointsId).Should().Be(1000m);
        Outbox("transfer_failed:t-x")!.Payload.Should().Contain("no_rule");
        // A refused transfer creates no account for the receiver.
        _db.CustomerAccounts.AsNoTracking().Any(a => a.TenantId == _tenantId && a.ContactKey == "receiver2").Should().BeFalse();
    }

    // R-O12: the winning rule's limit decides; a looser lower-priority rule is never tried.
    [Fact]
    public async Task Over_the_winning_rules_limit_fails_without_trying_a_lower_priority_rule()
    {
        await AddRuleAsync(maxPerDay: 100m, priority: 50);
        await AddRuleAsync(maxPerDay: 100000m, priority: 1);
        await SeedAsync("sender3", _pointsId, 1000m);
        var (handler, _) = Build();

        await handler.HandleAsync(Transfer("t-prio", "sender3", "receiver3", "500", _pointsId), CancellationToken.None);

        Balance("sender3", _pointsId).Should().Be(1000m);
        Outbox("transfer_failed:t-prio")!.Payload.Should().Contain("daily_limit_exceeded");
    }

    [Fact]
    public async Task A_rule_whose_cooldown_is_active_is_reported_as_rule_limit_reached()
    {
        await AddRuleAsync(maxPerDay: 10000m, limits: new RuleLimits { CooldownHours = 24m });
        await SeedAsync("sender4", _pointsId, 1000m);
        var (handler, _) = Build();

        await handler.HandleAsync(Transfer("t-c1", "sender4", "receiver4", "100", _pointsId), CancellationToken.None);
        await handler.HandleAsync(Transfer("t-c2", "sender4", "receiver4", "100", _pointsId), CancellationToken.None);

        Balance("sender4", _pointsId).Should().Be(900m);
        Outbox("transfer_failed:t-c2")!.Payload.Should().Contain("rule_limit_reached");
    }

    [Fact]
    public async Task A_program_that_is_not_live_refuses_the_transfer()
    {
        await AddRuleAsync(maxPerDay: 10000m);
        await SeedAsync("sender5", _pointsId, 1000m);
        await _db.Programs.Where(p => p.Id == _programId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, ProgramStatus.Inactive));
        var (handler, _) = Build();

        await handler.HandleAsync(Transfer("t-off", "sender5", "receiver5", "100", _pointsId), CancellationToken.None);

        Balance("sender5", _pointsId).Should().Be(1000m);
        Outbox("transfer_failed:t-off")!.Payload.Should().Contain("program_not_live");
    }

    [Fact]
    public async Task A_redelivered_transfer_moves_points_once_and_audits_once()
    {
        await AddRuleAsync(maxPerDay: 10000m);
        await SeedAsync("sender6", _pointsId, 1000m);
        var (handler, _) = Build();
        var envelope = Transfer("t-again", "sender6", "receiver6", "300", _pointsId);

        await handler.HandleAsync(envelope, CancellationToken.None);
        await handler.HandleAsync(envelope, CancellationToken.None);

        Balance("sender6", _pointsId).Should().Be(700m);
        Balance("receiver6", _pointsId).Should().Be(300m);
        _db.RuleFireAudits.AsNoTracking().Count(a => a.SourceEventId == "t-again").Should().Be(1);
        Outbox("transfer_failed:t-again").Should().BeNull();
    }
}
