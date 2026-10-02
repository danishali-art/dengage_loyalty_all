using System.Text.Json;
using dEngage.Loyalty.Consumer.Handlers;
using dEngage.Loyalty.Ledger;
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
// PointsRedeemHandler takes a row lock (SELECT ... FOR UPDATE), which Sqlite can't run, so this
// needs a real Postgres. Requires Docker. Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class PointsRedeemOutcomeE2ETests : IAsyncLifetime
{
    private const string Tenant = "t1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private LoyaltyDbContext _db = default!;
    private Guid _tenantId;
    private Guid _pointsId;
    private Guid _cashId;
    private Guid _unconfiguredPointsId;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<LoyaltyDbContext>().UseNpgsql(_container.GetConnectionString()).Options;
        _db = new LoyaltyDbContext(options);
        await _db.Database.MigrateAsync();

        _tenantId = Guid.NewGuid();
        _db.Tenants.Add(new Tenant { Id = _tenantId, Slug = Tenant, Name = Tenant, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });

        var programId = Guid.NewGuid();
        _db.Programs.Add(new ProgramEntity { Id = programId, TenantId = _tenantId, Name = "p1", Status = ProgramStatus.Active, CreatedAt = DateTime.UtcNow });

        _cashId = Guid.NewGuid();
        _pointsId = Guid.NewGuid();
        _unconfiguredPointsId = Guid.NewGuid();
        _db.AccountTypes.AddRange(
            new AccountTypeEntity
            {
                Id = _cashId, TenantId = _tenantId, ProgramId = programId, Type = "CASH", Name = "Cash",
                Config = """{"currency": "SAR", "decimals": 2}""", CreatedAt = DateTime.UtcNow
            },
            new AccountTypeEntity
            {
                Id = _pointsId, TenantId = _tenantId, ProgramId = programId, Type = "POINTS", Name = "Points",
                Config = $$$"""{"redemption": {"target_account_type_id": "{{{_cashId}}}", "rate": 0.01, "min_points": 100}}""",
                CreatedAt = DateTime.UtcNow
            },
            new AccountTypeEntity
            {
                Id = _unconfiguredPointsId, TenantId = _tenantId, ProgramId = programId, Type = "POINTS", Name = "No redemption",
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
        return (new PointsRedeemHandler(ledger, new OutboxService(_db, resolver), _db, resolver), ledger);
    }

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

    [Fact]
    public async Task Successful_redeem_moves_points_to_cash_and_publishes_points_redeemed()
    {
        await SeedPointsAsync("ok", 500m);
        var (handler, _) = Build();

        await handler.HandleAsync(Redeem("e-ok", "ok", "300", _pointsId), CancellationToken.None);

        Balance("ok", _pointsId).Should().Be(200m);
        Balance("ok", _cashId).Should().Be(3.00m);
        var redeemed = Outbox("points_redeemed:e-ok");
        redeemed.Should().NotBeNull();
        redeemed!.EventType.Should().Be(OutboundEventTypes.PointsRedeemed);
        Outbox("redeem_failed:e-ok").Should().BeNull();
    }

    [Fact]
    public async Task Insufficient_points_is_reported_with_redeem_failed_instead_of_throwing()
    {
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
    public async Task Below_minimum_is_reported_with_redeem_failed_instead_of_throwing()
    {
        await SeedPointsAsync("tiny", 500m);
        var (handler, _) = Build();

        var act = () => handler.HandleAsync(Redeem("e-tiny", "tiny", "50", _pointsId), CancellationToken.None);

        await act.Should().NotThrowAsync();
        Balance("tiny", _pointsId).Should().Be(500m);
        Outbox("redeem_failed:e-tiny")!.Payload.Should().Contain("below_minimum");
    }

    // Regression guard for the redelivery check added with the failure event: a redelivered
    // event (crash after commit, before the inbox update) sees the already-debited balance and,
    // without the check, would announce a false redeem_failed.
    [Fact]
    public async Task Redelivered_successful_redeem_posts_once_and_never_reports_a_failure()
    {
        await SeedPointsAsync("again", 500m);
        var (handler, _) = Build();
        var envelope = Redeem("e-again", "again", "300", _pointsId);

        await handler.HandleAsync(envelope, CancellationToken.None);
        await handler.HandleAsync(envelope, CancellationToken.None);

        Balance("again", _pointsId).Should().Be(200m);
        Balance("again", _cashId).Should().Be(3.00m);
        _db.LedgerEntries.AsNoTracking().Count(l => l.TenantId == Tenant && l.SourceEventId == "e-again").Should().Be(2); // debit + credit, once
        Outbox("redeem_failed:e-again").Should().BeNull();
    }

    [Fact]
    public async Task A_configuration_error_still_fails_the_event()
    {
        var (handler, _) = Build();

        var act = () => handler.HandleAsync(Redeem("e-cfg", "cfg", "100", _unconfiguredPointsId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("redemption_not_configured*");
        Outbox("redeem_failed:e-cfg").Should().BeNull();
    }
}
