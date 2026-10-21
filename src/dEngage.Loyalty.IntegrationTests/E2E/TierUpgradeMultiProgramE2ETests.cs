using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine;
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

// One event runs the rule engine once per active program (CampaignEvaluationService), and each
// run re-evaluates that program's tier with the same source event id. The tier_upgrade_log race
// guard used to be unique on (tenant, contact, event), so the second program's upgrade hit
// ux_tier_upgrade_log_source_event, was swallowed by TierEvaluationService and lost together with
// its tier.changed event. Real Postgres: TierEvaluationService sums decimals, which Sqlite can't.
// Requires Docker. Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class TierUpgradeMultiProgramE2ETests : IAsyncLifetime
{
    private const string Tenant = "t1";
    private const string Contact = "customer_1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private LoyaltyDbContext _db = default!;
    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<LoyaltyDbContext>().UseNpgsql(_container.GetConnectionString()).Options;
        _db = new LoyaltyDbContext(options);
        await _db.Database.MigrateAsync();

        _tenantId = Guid.NewGuid();
        _db.Tenants.Add(new Tenant { Id = _tenantId, Slug = Tenant, Name = Tenant, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
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

    // A published program with a tier-qualifying POINTS account and a single tier open to everyone.
    private async Task<(Guid ProgramId, Guid PointsId, Guid TierId)> AddProgramAsync(string slug)
    {
        var programId = Guid.NewGuid();
        var pointsId = Guid.NewGuid();
        var tierId = Guid.NewGuid();
        _db.Programs.Add(new ProgramEntity
        {
            Id = programId, TenantId = _tenantId, Name = slug, Slug = slug, Status = ProgramStatus.Active,
            PublicationStatus = ProgramPublicationStatus.Published, CreatedAt = DateTime.UtcNow
        });
        _db.AccountTypes.Add(new AccountTypeEntity
        {
            Id = pointsId, TenantId = _tenantId, ProgramId = programId, Type = "POINTS", Name = $"{slug} points",
            Config = """{"decimals": 0}""", IsTierQualifying = true, CreatedAt = DateTime.UtcNow
        });
        _db.TierDefinitions.Add(new TierDefinition
        {
            Id = tierId, TenantId = _tenantId, ProgramId = programId, Name = $"{slug}_member", DisplayName = "Member",
            MinPoints = 0, QualifyingDays = 30, SortOrder = 1, CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return (programId, pointsId, tierId);
    }

    [Fact]
    public async Task One_event_upgrades_the_tier_in_every_program_it_earns_in()
    {
        var shop = await AddProgramAsync("shop");
        var wallet = await AddProgramAsync("wallet");

        var resolver = new TenantSlugResolver(_db, new TenantSlugCache());
        var ledger = new LedgerService(_db, resolver);
        var tierEval = new TierEvaluationService(_db, new OutboxService(_db, resolver), resolver,
            NullLogger<TierEvaluationService>.Instance);

        // What the engine does per program for one order: post the earn, then re-evaluate the tier.
        var accountIds = new List<Guid>();
        foreach (var (programId, pointsId, _) in new[] { shop, wallet })
        {
            var account = await ledger.UpsertAccountAsync(Tenant, Contact, pointsId);
            await ledger.AddEntryAsync(Tenant, account.Id, Contact, 500m, LedgerReason.Earn,
                sourceEventId: "order-1", idempotencyKey: $"order-1:{pointsId}");
            await tierEval.EvaluateAsync(Tenant, Contact, programId, pointsId, "order-1");
            accountIds.Add(account.Id);
        }

        var tiers = await _db.CustomerAccounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.AccountTypeId, a => a.TierId);
        tiers[shop.PointsId].Should().Be(shop.TierId);
        tiers[wallet.PointsId].Should().Be(wallet.TierId);

        (await _db.TierUpgradeLogs.AsNoTracking()
                .Where(l => l.TenantId == _tenantId && l.ContactKey == Contact && l.SourceEventId == "order-1")
                .Select(l => l.ToTierId).ToListAsync())
            .Should().BeEquivalentTo([shop.TierId, wallet.TierId]);

        foreach (var accountId in accountIds)
            (await _db.OutboxEvents.AsNoTracking().AnyAsync(o => o.DedupKey == $"tier_changed:order-1:{accountId}"))
                .Should().BeTrue("each program's tier change publishes its own tier.changed");
    }

    // The guard still holds for one program: a redelivery doesn't log the same upgrade twice.
    [Fact]
    public async Task A_redelivered_event_logs_one_tier_change_per_program()
    {
        var shop = await AddProgramAsync("shop");

        var resolver = new TenantSlugResolver(_db, new TenantSlugCache());
        var ledger = new LedgerService(_db, resolver);
        var tierEval = new TierEvaluationService(_db, new OutboxService(_db, resolver), resolver,
            NullLogger<TierEvaluationService>.Instance);

        var account = await ledger.UpsertAccountAsync(Tenant, Contact, shop.PointsId);
        await ledger.AddEntryAsync(Tenant, account.Id, Contact, 500m, LedgerReason.Earn,
            sourceEventId: "order-1", idempotencyKey: "order-1");
        await tierEval.EvaluateAsync(Tenant, Contact, shop.ProgramId, shop.PointsId, "order-1");
        await tierEval.EvaluateAsync(Tenant, Contact, shop.ProgramId, shop.PointsId, "order-1"); // redelivery

        (await _db.TierUpgradeLogs.AsNoTracking().CountAsync(l => l.TenantId == _tenantId && l.SourceEventId == "order-1"))
            .Should().Be(1);
    }
}
