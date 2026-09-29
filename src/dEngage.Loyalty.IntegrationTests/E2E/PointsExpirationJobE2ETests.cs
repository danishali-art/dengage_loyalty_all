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

// Tiqmo TQ05 — PointsExpirationJob runs hand-written raw SQL (window functions, CTEs with
// INSERT/UPDATE, jsonb_build_object) that Sqlite cannot execute, so unlike the other Engine
// tests this one needs a real Postgres. Excluded from the default `dotnet test` run — requires
// Docker. Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class PointsExpirationJobE2ETests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private LoyaltyDbContext _db = default!;
    private Guid _tenantId;
    private Guid _programId;
    private Guid _pointsAccountTypeId;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<LoyaltyDbContext>().UseNpgsql(_container.GetConnectionString()).Options;
        _db = new LoyaltyDbContext(options);
        await _db.Database.MigrateAsync();

        _tenantId = Guid.NewGuid();
        _db.Tenants.Add(new Tenant { Id = _tenantId, Slug = "t1", Name = "t1", Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });

        _programId = Guid.NewGuid();
        _db.Programs.Add(new ProgramEntity { Id = _programId, TenantId = _tenantId, Name = "p1", Status = ProgramStatus.Active, CreatedAt = DateTime.UtcNow });

        _pointsAccountTypeId = Guid.NewGuid();
        _db.AccountTypes.Add(new AccountTypeEntity
        {
            Id = _pointsAccountTypeId,
            TenantId = _tenantId,
            ProgramId = _programId,
            Type = "POINTS",
            Name = "FinPuan",
            Config = """{"expiration_days": 180}""",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // ledger_entries is list-partitioned by tenant slug in production (see
        // PlatformAppService.CreateTenantAsync) — migrations create the partitioned parent table
        // but not per-tenant partitions, so a directly-inserted tenant needs one here too or every
        // insert into ledger_entries fails with "no partition of relation found for row".
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS ledger_entries_t1 PARTITION OF ledger_entries FOR VALUES IN ('t1')");
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task SeedAccountWithEarnsAsync(string contactKey, params (decimal amount, int daysAgo)[] earns)
    {
        var account = new CustomerAccount
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ContactKey = contactKey,
            AccountTypeId = _pointsAccountTypeId,
            Balance = earns.Sum(e => e.amount),
            UpdatedAt = DateTime.UtcNow
        };
        _db.CustomerAccounts.Add(account);
        await _db.SaveChangesAsync();

        foreach (var (amount, daysAgo) in earns)
        {
            _db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                TenantId = "t1",
                CustomerAccountId = account.Id,
                ContactKey = contactKey,
                Delta = amount,
                Reason = LedgerReason.Earn,
                SourceEventId = "seed",
                IdempotencyKey = $"seed:{contactKey}:{daysAgo}d:{amount}",
                CreatedAt = DateTime.UtcNow.AddDays(-daysAgo)
            });
        }
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Old_points_expire_fifo_after_180_days_while_fresh_points_are_preserved()
    {
        await SeedAccountWithEarnsAsync("tq_g", (800m, 400), (300m, 100));

        var job = new PointsExpirationJob(_db, NullLogger<PointsExpirationJob>.Instance);
        await job.RunAsync();

        var balance = await _db.CustomerAccounts
            .Where(a => a.TenantId == _tenantId && a.ContactKey == "tq_g" && a.AccountTypeId == _pointsAccountTypeId)
            .Select(a => a.Balance)
            .SingleAsync();
        balance.Should().Be(300m);

        var expiredCount = await _db.LedgerEntries.CountAsync(l =>
            l.ContactKey == "tq_g" && l.Reason == "points_expired" && l.Delta == -800m);
        expiredCount.Should().Be(1);
    }

    [Fact]
    public async Task Running_the_job_twice_the_same_day_does_not_expire_the_same_points_again()
    {
        await SeedAccountWithEarnsAsync("tq_g2", (800m, 400));

        var job = new PointsExpirationJob(_db, NullLogger<PointsExpirationJob>.Instance);
        await job.RunAsync();
        await job.RunAsync();

        var expiredCount = await _db.LedgerEntries.CountAsync(l => l.ContactKey == "tq_g2" && l.Reason == "points_expired");
        expiredCount.Should().Be(1);
    }
}
