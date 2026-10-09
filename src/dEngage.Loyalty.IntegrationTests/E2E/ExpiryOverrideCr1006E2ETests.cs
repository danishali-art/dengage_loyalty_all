using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.IntegrationTests.E2E;

// CR 2026-10-06 Phase 5 (docs/scope-changes/2026-10-05-rule-config-limits-by-trigger.md §3.9):
// expiry by each lot's own date (ledger_entries.expires_at, or earn date + the wallet's
// expiration_days), soonest-expiring points spent first (E3). PointsExpirationJob and
// PointsExpiringDetectorJob are Postgres SQL, so this needs a real Postgres. Requires Docker.
// Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class ExpiryOverrideCr1006E2ETests : IAsyncLifetime
{
    private const string Tenant = "t1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private LoyaltyDbContext _db = default!;
    private Guid _tenantId;
    private Guid _wallet365;
    private Guid _walletNoExpiry;
    private Guid _cash;

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

        _wallet365 = Guid.NewGuid();
        _walletNoExpiry = Guid.NewGuid();
        _cash = Guid.NewGuid();
        _db.AccountTypes.AddRange(
            new AccountTypeEntity { Id = _wallet365, TenantId = _tenantId, ProgramId = programId, Type = "POINTS", Name = "Points365",
                Config = """{"expiration_days": 365, "warning_days": 30}""", CreatedAt = DateTime.UtcNow },
            new AccountTypeEntity { Id = _walletNoExpiry, TenantId = _tenantId, ProgramId = programId, Type = "POINTS", Name = "PointsForever",
                Config = "{}", CreatedAt = DateTime.UtcNow },
            new AccountTypeEntity { Id = _cash, TenantId = _tenantId, ProgramId = programId, Type = "CASH", Name = "Cash",
                Config = """{"currency": "SAR", "decimals": 2}""", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS ledger_entries_t1 PARTITION OF ledger_entries FOR VALUES IN ('t1')");
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    private sealed record Lot(decimal Amount, int DaysAgo, int? ExpiresInDays = null, string Reason = LedgerReason.Earn);

    // Seeds entries directly (as a past posting would have written them): ExpiresInDays null =
    // no expires_at, i.e. an entry from before Phase 5 or one dated by the wallet.
    private async Task<Guid> SeedAsync(string contactKey, Guid wallet, params Lot[] lots)
    {
        var account = new CustomerAccount
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ContactKey = contactKey, AccountTypeId = wallet,
            Balance = lots.Sum(l => l.Amount), UpdatedAt = DateTime.UtcNow
        };
        _db.CustomerAccounts.Add(account);
        await _db.SaveChangesAsync();

        var n = 0;
        foreach (var lot in lots)
        {
            _db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(), TenantId = Tenant, CustomerAccountId = account.Id, ContactKey = contactKey,
                Delta = lot.Amount, Reason = lot.Reason, SourceEventId = "seed", IdempotencyKey = $"seed:{contactKey}:{n++}",
                CreatedAt = DateTime.UtcNow.AddDays(-lot.DaysAgo),
                ExpiresAt = lot.ExpiresInDays is { } d ? DateTime.UtcNow.AddDays(d) : null
            });
        }
        await _db.SaveChangesAsync();
        return account.Id;
    }

    private Task RunExpiryAsync() => new PointsExpirationJob(_db, NullLogger<PointsExpirationJob>.Instance).RunAsync();

    private decimal Balance(Guid accountId) =>
        _db.CustomerAccounts.AsNoTracking().Single(a => a.Id == accountId).Balance;

    private decimal Expired(string contactKey) =>
        _db.LedgerEntries.AsNoTracking().Where(l => l.ContactKey == contactKey && l.Reason == LedgerReason.PointsExpired)
            .Select(l => l.Delta).ToList().Sum();

    [Fact]
    public async Task A_lot_with_an_override_expires_on_its_own_date_while_wallet_dated_lots_wait()
    {
        var account = await SeedAsync("c1", _wallet365,
            new Lot(100m, DaysAgo: 40),                       // wallet: expires in 325 days
            new Lot(50m, DaysAgo: 40, ExpiresInDays: -10));    // a 30-day override, expired 10 days ago

        await RunExpiryAsync();

        Expired("c1").Should().Be(-50m);
        Balance(account).Should().Be(100m);
    }

    [Fact]
    public async Task An_override_longer_than_the_wallet_keeps_points_past_the_wallets_expiry()
    {
        var account = await SeedAsync("c2", _wallet365,
            new Lot(100m, DaysAgo: 400, ExpiresInDays: 100),   // a 500-day override (E1)
            new Lot(30m, DaysAgo: 400));                       // wallet: expired 35 days ago

        await RunExpiryAsync();

        Expired("c2").Should().Be(-30m);
        Balance(account).Should().Be(100m);
    }

    [Fact]
    public async Task An_override_dates_points_in_a_wallet_without_expiry()
    {
        var account = await SeedAsync("c3", _walletNoExpiry,
            new Lot(70m, DaysAgo: 31, ExpiresInDays: -1),      // promo points with a 30-day override (E2)
            new Lot(40m, DaysAgo: 400));                       // never expire

        await RunExpiryAsync();

        Expired("c3").Should().Be(-70m);
        Balance(account).Should().Be(40m);
    }

    // E3: a redemption uses the points that expire soonest. Oldest-first would have used the
    // year-old lot and let the short promo lot expire.
    [Fact]
    public async Task Spending_uses_the_soonest_expiring_points_first()
    {
        var account = await SeedAsync("c4", _wallet365,
            new Lot(100m, DaysAgo: 200),                        // wallet: expires in 165 days
            new Lot(100m, DaysAgo: 10, ExpiresInDays: -1),      // promo lot, expired yesterday
            new Lot(-100m, DaysAgo: 5, Reason: LedgerReason.PointsRedeemed));

        await RunExpiryAsync();

        Expired("c4").Should().Be(0m, "the redemption already used the promo lot");
        Balance(account).Should().Be(100m);
    }

    [Fact]
    public async Task Without_overrides_the_result_is_the_old_oldest_first_expiry()
    {
        var account = await SeedAsync("c5", _wallet365,
            new Lot(100m, DaysAgo: 400),
            new Lot(100m, DaysAgo: 380),
            new Lot(50m, DaysAgo: 10),
            new Lot(-120m, DaysAgo: 5, Reason: LedgerReason.PointsRedeemed));

        await RunExpiryAsync();

        // Oldest-first: the 120 redeemed came from the 400-day lot (100) and the 380-day lot (20);
        // the 80 left of the 380-day lot expires.
        Expired("c5").Should().Be(-80m);
        Balance(account).Should().Be(50m);
    }

    [Fact]
    public async Task The_expiring_soon_warning_covers_a_lot_dated_by_an_override()
    {
        var account = await SeedAsync("c6", _wallet365, new Lot(60m, DaysAgo: 5, ExpiresInDays: 10));

        await new PointsExpiringDetectorJob(_db, NullLogger<PointsExpiringDetectorJob>.Instance).RunAsync();

        var expiresOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)).ToString("yyyy-MM-dd");
        var warning = _db.OutboxEvents.AsNoTracking().SingleOrDefault(o => o.DedupKey == $"expiring:{account}:{expiresOn}");
        warning.Should().NotBeNull();
        warning!.Payload.Should().Contain("\"60.00\"").And.Contain(expiresOn);
    }

    [Fact]
    public async Task Postings_record_their_expiry_date_from_the_wallet_or_the_override()
    {
        var ledger = new LedgerService(_db, new TenantSlugResolver(_db, new TenantSlugCache()));
        var points = await ledger.UpsertAccountAsync(Tenant, "c7", _wallet365);
        var forever = await ledger.UpsertAccountAsync(Tenant, "c7", _walletNoExpiry);
        var cash = await ledger.UpsertAccountAsync(Tenant, "c7", _cash);
        var before = DateTime.UtcNow;
        var overrideDate = before.AddDays(30);

        var walletDated = await ledger.AddEntryAsync(Tenant, points.Id, "c7", 10m, LedgerReason.Earn, "e1", "k1");
        var transferIn = await ledger.AddEntryAsync(Tenant, points.Id, "c7", 10m, LedgerReason.TransferIn, "e2", "k2");
        var overridden = await ledger.AddEntryAsync(Tenant, points.Id, "c7", 10m, LedgerReason.Earn, "e3", "k3", expiresAt: overrideDate);
        var noWalletExpiry = await ledger.AddEntryAsync(Tenant, forever.Id, "c7", 10m, LedgerReason.Earn, "e4", "k4");
        var cashEntry = await ledger.AddEntryAsync(Tenant, cash.Id, "c7", 10m, LedgerReason.Earn, "e5", "k5");
        var redemption = await ledger.AddEntryAsync(Tenant, points.Id, "c7", -5m, LedgerReason.PointsRedeemed, "e6", "k6");
        // A rule saved before its target was validated can still carry an override onto cash.
        var cashWithOverride = await ledger.AddEntryAsync(Tenant, cash.Id, "c7", 10m, LedgerReason.Earn, "e7", "k7", expiresAt: overrideDate);

        walletDated.ExpiresAt.Should().BeCloseTo(before.AddDays(365), TimeSpan.FromMinutes(1));
        transferIn.ExpiresAt.Should().BeCloseTo(before.AddDays(365), TimeSpan.FromMinutes(1));
        overridden.ExpiresAt.Should().Be(overrideDate);
        noWalletExpiry.ExpiresAt.Should().BeNull();
        cashEntry.ExpiresAt.Should().BeNull("cash never expires");
        redemption.ExpiresAt.Should().BeNull("only earn / transfer_in entries are lots");
        cashWithOverride.ExpiresAt.Should().BeNull("cash never expires, even through a rule's override");
    }
}
