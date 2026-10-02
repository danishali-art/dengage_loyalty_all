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

// CR 2026-09-30 addendum A, end to end: the `transfer.daily_limit` the account-type screen now
// saves is exactly what PointsTransferHandler enforces. Real Postgres — the handler row-locks
// both accounts (FOR UPDATE). Requires Docker. Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class PointsTransferLimitE2ETests : IAsyncLifetime
{
    private const string Tenant = "t1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private LoyaltyDbContext _db = default!;
    private Guid _tenantId;
    private Guid _limitedId;
    private Guid _noTransferId;

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

        _limitedId = Guid.NewGuid();
        _noTransferId = Guid.NewGuid();
        _db.AccountTypes.AddRange(
            // The shape the portal's account-type form writes when "Allow points transfer" is on.
            new AccountTypeEntity { Id = _limitedId, TenantId = _tenantId, ProgramId = programId, Type = "POINTS", Name = "Points", Config = """{"decimals": 0, "transfer": {"daily_limit": 5000}}""", CreatedAt = DateTime.UtcNow },
            new AccountTypeEntity { Id = _noTransferId, TenantId = _tenantId, ProgramId = programId, Type = "POINTS", Name = "No transfer", Config = """{"decimals": 0}""", CreatedAt = DateTime.UtcNow });
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
        return (new PointsTransferHandler(ledger, new OutboxService(_db, resolver), _db, resolver), ledger);
    }

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

    [Fact]
    public async Task Transfers_up_to_the_daily_limit_succeed_and_the_next_one_is_refused()
    {
        await SeedAsync("sender", _limitedId, 10000m);
        var (handler, _) = Build();

        await handler.HandleAsync(Transfer("t-1", "sender", "receiver", "1500", _limitedId), CancellationToken.None);
        await handler.HandleAsync(Transfer("t-2", "sender", "receiver", "3500", _limitedId), CancellationToken.None); // exactly 5000 today
        await handler.HandleAsync(Transfer("t-3", "sender", "receiver", "1", _limitedId), CancellationToken.None);    // over the limit

        Balance("sender", _limitedId).Should().Be(5000m);
        Balance("receiver", _limitedId).Should().Be(5000m);
        var failed = _db.OutboxEvents.AsNoTracking().Single(o => o.TenantId == _tenantId && o.DedupKey == "transfer_failed:t-3");
        failed.EventType.Should().Be(OutboundEventTypes.PointsTransferFailed);
        failed.Payload.Should().Contain("daily_limit_exceeded");
    }

    [Fact]
    public async Task A_wallet_without_a_transfer_setting_refuses_transfers()
    {
        await SeedAsync("sender2", _noTransferId, 1000m);
        var (handler, _) = Build();

        var act = () => handler.HandleAsync(Transfer("t-x", "sender2", "receiver2", "100", _noTransferId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("transfer_not_configured*");
        Balance("sender2", _noTransferId).Should().Be(1000m);
    }
}
