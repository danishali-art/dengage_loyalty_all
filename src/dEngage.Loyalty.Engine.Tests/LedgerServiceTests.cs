using FluentAssertions;
using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SchemaAccountType = dEngage.Loyalty.Schema.Entities.AccountType;

namespace dEngage.Loyalty.Engine.Tests;

// Uses SQLite in-memory rather than the EF InMemory provider — LedgerService.AddEntryAsync
// relies on ExecuteUpdateAsync (a bulk operation), which the InMemory provider does not support.
public sealed class LedgerServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LoyaltyDbContext _db;
    private readonly LedgerService _sut;
    private readonly Guid _accountId = Guid.NewGuid();

    public LedgerServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LoyaltyDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new LoyaltyDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new LedgerService(_db, new TenantSlugResolver(_db, new TenantSlugCache()));

        var tenantGuid = Guid.NewGuid();
        _db.Tenants.Add(new Tenant { Id = tenantGuid, Slug = "t1", Name = "t1", Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
        var programId = Guid.NewGuid();
        _db.Programs.Add(new Schema.Entities.Program { Id = programId, TenantId = tenantGuid, Name = "Loyalty" });
        var accountTypeId = Guid.NewGuid();
        _db.AccountTypes.Add(new SchemaAccountType { Id = accountTypeId, TenantId = tenantGuid, ProgramId = programId, Type = "POINTS", Name = "Points" });
        _db.CustomerAccounts.Add(new CustomerAccount { Id = _accountId, TenantId = tenantGuid, ContactKey = "c1", AccountTypeId = accountTypeId, Balance = 0 });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task AddEntryAsync_inserts_entry_and_applies_delta_to_balance()
    {
        await _sut.AddEntryAsync("t1", _accountId, "c1", 15m, LedgerReason.Earn, "evt-1", "evt-1:rule-1");

        // AddEntryAsync updates the balance via ExecuteUpdateAsync, a bulk operation that bypasses
        // the change tracker — a subsequent read on the same DbContext instance would otherwise
        // return the now-stale tracked CustomerAccount (see LedgerPoster.cs's identical caveat).
        // A real request never hits this: each gets its own fresh DbContext/scope.
        _db.ChangeTracker.Clear();
        var balance = await _sut.GetBalanceAsync("t1", "c1", (await _db.CustomerAccounts.FindAsync(_accountId))!.AccountTypeId);
        balance.Should().Be(15m);

        var entries = await _db.LedgerEntries.Where(x => x.TenantId == "t1").ToListAsync();
        entries.Should().ContainSingle(x => x.Delta == 15m && x.Reason == LedgerReason.Earn);
    }

    [Fact]
    public async Task AddEntryAsync_with_a_repeated_idempotency_key_returns_the_existing_entry_without_reapplying_the_delta()
    {
        var first = await _sut.AddEntryAsync("t1", _accountId, "c1", 15m, LedgerReason.Earn, "evt-1", "evt-1:rule-1");

        var second = await _sut.AddEntryAsync("t1", _accountId, "c1", 15m, LedgerReason.Earn, "evt-1", "evt-1:rule-1");

        second.Id.Should().Be(first.Id);

        _db.ChangeTracker.Clear();
        var balance = await _sut.GetBalanceAsync("t1", "c1", (await _db.CustomerAccounts.FindAsync(_accountId))!.AccountTypeId);
        balance.Should().Be(15m); // not 30m — the second call must be a no-op

        var entries = await _db.LedgerEntries.Where(x => x.TenantId == "t1").ToListAsync();
        entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddEntryAsync_applies_a_negative_delta_correctly()
    {
        await _sut.AddEntryAsync("t1", _accountId, "c1", 100m, LedgerReason.CashLoad, "evt-1", "evt-1:load");
        _db.ChangeTracker.Clear();
        await _sut.AddEntryAsync("t1", _accountId, "c1", -40m, LedgerReason.CashSpend, "evt-2", "evt-2:spend");

        _db.ChangeTracker.Clear();
        var balance = await _sut.GetBalanceAsync("t1", "c1", (await _db.CustomerAccounts.FindAsync(_accountId))!.AccountTypeId);
        balance.Should().Be(60m);
    }

    [Fact]
    public async Task UpsertAccountAsync_returns_the_existing_account_when_one_already_exists()
    {
        var accountTypeId = (await _db.CustomerAccounts.FindAsync(_accountId))!.AccountTypeId;

        var account = await _sut.UpsertAccountAsync("t1", "c1", accountTypeId);

        account.Id.Should().Be(_accountId);
        (await _db.CustomerAccounts.CountAsync()).Should().Be(1); // no duplicate created
    }

    [Fact]
    public async Task UpsertAccountAsync_creates_a_new_zero_balance_account_when_none_exists()
    {
        var accountTypeId = (await _db.CustomerAccounts.FindAsync(_accountId))!.AccountTypeId;

        var account = await _sut.UpsertAccountAsync("t1", "new_contact", accountTypeId);

        account.Balance.Should().Be(0);
        account.ContactKey.Should().Be("new_contact");
    }
}
