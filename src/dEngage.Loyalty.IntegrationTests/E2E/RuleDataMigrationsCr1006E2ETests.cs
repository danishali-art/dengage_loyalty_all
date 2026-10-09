using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.E2E;

// CR 2026-10-06 data migrations, run on a real Postgres from the migration before them:
// - DisableReversalRulesOnOrderRefundedCr1006 (D22): Reversal rules on order.refunded.
// - DisableTestModeEarnRulesCr1006 (D21): earn rules with a stored testMode flag — they would
//   otherwise start paying when the engine stops honouring it (D20).
// Each disables exactly its rows, never deletes, and flags a published program as changed.
// Requires Docker. Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class RuleDataMigrationsCr1006E2ETests : IAsyncLifetime
{
    private const string BeforeDataMigrations = "20261008101744_HeldPostingRefundsCr1006";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private LoyaltyDbContext _db = default!;
    private Guid _tenantId;
    private Guid _programId;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var options = new DbContextOptionsBuilder<LoyaltyDbContext>().UseNpgsql(_container.GetConnectionString()).Options;
        _db = new LoyaltyDbContext(options);
        await _db.GetService<IMigrator>().MigrateAsync(BeforeDataMigrations);

        _tenantId = Guid.NewGuid();
        _db.Tenants.Add(new Tenant { Id = _tenantId, Slug = "t1", Name = "t1", Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        // Raw SQL: the current EF model maps columns added by later migrations (e.g.
        // programs.default_rounding, Phase 4) that don't exist yet at this point in the history.
        _programId = Guid.NewGuid();
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO programs (id, tenant_id, name, status, publication_status, has_unpublished_changes, slug, created_at)
            VALUES ({_programId}, {_tenantId}, 'p1', {ProgramStatus.Active}, {ProgramPublicationStatus.Published}, false, 'p1', {DateTime.UtcNow})
            """);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    private Guid Seed(string type, string trigger, string status, string? configuration)
    {
        var id = Guid.NewGuid();
        _db.Rules.Add(new Rule
        {
            Id = id, TenantId = _tenantId, ProgramId = _programId, Name = $"{type}-{trigger}-{status}",
            Type = type, Trigger = trigger, Calculation = "{}", Configuration = configuration,
            Priority = 10, Status = status, CurrentVersion = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        return id;
    }

    private string StatusOf(Guid id) => _db.Rules.AsNoTracking().Single(r => r.Id == id).Status;

    [Fact]
    public async Task The_data_migrations_disable_exactly_the_intended_rules()
    {
        const string testModeOn = """{"posting":"Immediate","reversible":true,"testMode":true,"notifyOnAward":false}""";
        const string testModeOff = """{"posting":"Immediate","reversible":true,"testMode":false,"notifyOnAward":false}""";

        var earnTestActive = Seed(RuleTypes.FixedBonusRule, EventTypes.CardTransaction, RuleStatus.Active, testModeOn);
        var spendTestPending = Seed(RuleTypes.SpendRule, EventTypes.OrderCreated, RuleStatus.PendingApproval, testModeOn);
        var manualTestActive = Seed(RuleTypes.ManualAdjustmentRule, EventTypes.PointsAdjusted, RuleStatus.Active, testModeOn);
        var earnLive = Seed(RuleTypes.FixedBonusRule, EventTypes.CardTransaction, RuleStatus.Active, testModeOff);
        var earnNoConfig = Seed(RuleTypes.SpendRule, EventTypes.OrderCreated, RuleStatus.Active, null);
        var redeemTest = Seed(RuleTypes.RedemptionRule, EventTypes.PointsRedeem, RuleStatus.Active, testModeOn);
        var reversalOnRefund = Seed(RuleTypes.ReversalRule, EventTypes.OrderRefunded, RuleStatus.Active, null);
        var reversalPending = Seed(RuleTypes.ReversalRule, EventTypes.OrderRefunded, RuleStatus.PendingApproval, null);
        var reversalGeneric = Seed(RuleTypes.ReversalRule, "tenant.order_cancelled", RuleStatus.Active, null);
        var earnTestDeleted = Seed(RuleTypes.FixedBonusRule, EventTypes.CardTransaction, RuleStatus.Deleted, testModeOn);
        await _db.SaveChangesAsync();

        await _db.Database.MigrateAsync();

        // D21: earn rules with test mode on, active or pending.
        StatusOf(earnTestActive).Should().Be(RuleStatus.Disabled);
        StatusOf(spendTestPending).Should().Be(RuleStatus.Disabled);
        StatusOf(manualTestActive).Should().Be(RuleStatus.Disabled);
        // D22: Reversal rules on order.refunded, active or pending.
        StatusOf(reversalOnRefund).Should().Be(RuleStatus.Disabled);
        StatusOf(reversalPending).Should().Be(RuleStatus.Disabled);
        // Left alone.
        StatusOf(earnLive).Should().Be(RuleStatus.Active);
        StatusOf(earnNoConfig).Should().Be(RuleStatus.Active);
        StatusOf(redeemTest).Should().Be(RuleStatus.Active, "a burn rule's flag was already ignored — nothing changes for it");
        StatusOf(reversalGeneric).Should().Be(RuleStatus.Active, "Reversal rules stay available for tenant-defined events");
        StatusOf(earnTestDeleted).Should().Be(RuleStatus.Deleted);

        _db.Rules.AsNoTracking().Count(r => r.ProgramId == _programId).Should().Be(10, "nothing is deleted");
        _db.Programs.AsNoTracking().Single(p => p.Id == _programId).HasUnpublishedChanges.Should().BeTrue();
    }
}
