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

// CR 2026-10-05 item 5 (P-3): cash.added / cash.spent move nothing while the wallet's program
// isn't live (Active + Published) and say so with cash.add_failed / cash.spend_failed.
// CashSpentHandler row-locks the account (FOR UPDATE), so this needs a real Postgres.
// Requires Docker. Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class CashEventsProgramLiveE2ETests : IAsyncLifetime
{
    private const string Tenant = "t1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private LoyaltyDbContext _db = default!;
    private Guid _tenantId;
    private Guid _programId;
    private Guid _cashId;

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
        _db.AccountTypes.Add(new AccountTypeEntity
        {
            Id = _cashId, TenantId = _tenantId, ProgramId = _programId, Type = "CASH", Name = "Cash",
            Config = """{"currency": "SAR", "decimals": 2}""", CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS ledger_entries_t1 PARTITION OF ledger_entries FOR VALUES IN ('t1')");
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    private (CashAddedHandler Added, CashSpentHandler Spent) Build()
    {
        var resolver = new TenantSlugResolver(_db, new TenantSlugCache());
        var ledger = new LedgerService(_db, resolver);
        var outbox = new OutboxService(_db, resolver);
        return (new CashAddedHandler(ledger, outbox, _db, resolver), new CashSpentHandler(ledger, outbox, _db, resolver));
    }

    private EventEnvelope Cash(string eventType, string eventId, string contactKey, string amount) => new()
    {
        EventId = eventId,
        EventType = eventType,
        Tenant = Tenant,
        OccurredAt = DateTime.UtcNow,
        Data = JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            ["contact_key"] = contactKey,
            ["amount"] = amount,
            ["account_type_id"] = _cashId.ToString()
        })
    };

    private decimal Balance(string contactKey) =>
        _db.CustomerAccounts.AsNoTracking()
            .Where(a => a.TenantId == _tenantId && a.ContactKey == contactKey && a.AccountTypeId == _cashId)
            .Select(a => a.Balance).SingleOrDefault();

    private OutboxEvent? Outbox(string dedupKey) =>
        _db.OutboxEvents.AsNoTracking().SingleOrDefault(o => o.TenantId == _tenantId && o.DedupKey == dedupKey);

    private Task PauseAsync() =>
        _db.Programs.Where(p => p.Id == _programId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, ProgramStatus.Inactive));

    [Fact]
    public async Task Cash_moves_while_the_program_is_live()
    {
        var (added, spent) = Build();

        await added.HandleAsync(Cash(EventTypes.CashAdded, "c-add", "live", "100.50"), CancellationToken.None);
        await spent.HandleAsync(Cash(EventTypes.CashSpent, "c-spend", "live", "40"), CancellationToken.None);

        Balance("live").Should().Be(60.50m);
        Outbox("cash_add_failed:c-add").Should().BeNull();
        Outbox("cash_spend_failed:c-spend").Should().BeNull();
    }

    [Fact]
    public async Task A_cash_load_in_a_paused_program_is_refused_and_reported()
    {
        await PauseAsync();
        var (added, _) = Build();

        await added.HandleAsync(Cash(EventTypes.CashAdded, "c-add-off", "paused", "100"), CancellationToken.None);

        Balance("paused").Should().Be(0m);
        var failed = Outbox("cash_add_failed:c-add-off")!;
        failed.EventType.Should().Be(OutboundEventTypes.CashAddFailed);
        failed.Payload.Should().Contain("program_not_live");
        // A refused load creates no account row.
        _db.CustomerAccounts.AsNoTracking().Any(a => a.TenantId == _tenantId && a.ContactKey == "paused").Should().BeFalse();
    }

    [Fact]
    public async Task A_cash_spend_in_a_draft_program_is_refused_and_reported()
    {
        var (added, spent) = Build();
        await added.HandleAsync(Cash(EventTypes.CashAdded, "c-seed", "draft", "100"), CancellationToken.None);
        await _db.Programs.Where(p => p.Id == _programId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PublicationStatus, ProgramPublicationStatus.Draft));

        await spent.HandleAsync(Cash(EventTypes.CashSpent, "c-spend-off", "draft", "40"), CancellationToken.None);

        Balance("draft").Should().Be(100m);
        var failed = Outbox("cash_spend_failed:c-spend-off")!;
        failed.EventType.Should().Be(OutboundEventTypes.CashSpendFailed);
        failed.Payload.Should().Contain("program_not_live");
    }

    // A load posted before the program was paused, redelivered after: never a failure, never twice.
    [Fact]
    public async Task A_redelivered_cash_load_is_not_reported_as_failed_after_a_pause()
    {
        var (added, _) = Build();
        var envelope = Cash(EventTypes.CashAdded, "c-again", "again", "25");

        await added.HandleAsync(envelope, CancellationToken.None);
        await PauseAsync();
        await added.HandleAsync(envelope, CancellationToken.None);

        Balance("again").Should().Be(25m);
        Outbox("cash_add_failed:c-again").Should().BeNull();
    }
}
