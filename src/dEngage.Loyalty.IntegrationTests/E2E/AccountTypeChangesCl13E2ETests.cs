using dEngage.Loyalty.Ledger;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.RuleEngine.Models;
using dEngage.Loyalty.RuleEngine.Processing;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.IntegrationTests.E2E;

// 1.3.CL Phase 1 — the pieces that only run on Postgres: the raw-SQL jobs that now read the
// account type (PointsExpiringDetectorJob's warning_days, TierDowngradeJob's qualifying wallet)
// and the tier-qualifying lookup the engine does per event.
[Trait("Category", "E2E")]
public sealed class AccountTypeChangesCl13E2ETests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    private LoyaltyDbContext _db = default!;
    private Guid _tenantId;
    private Guid _programId;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _db = new LoyaltyDbContext(new DbContextOptionsBuilder<LoyaltyDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
        await _db.Database.MigrateAsync();

        _tenantId = Guid.NewGuid();
        _db.Tenants.Add(new Tenant { Id = _tenantId, Slug = "t1", Name = "t1", Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
        _programId = Guid.NewGuid();
        _db.Programs.Add(new ProgramEntity { Id = _programId, TenantId = _tenantId, Name = "p1", Status = ProgramStatus.Active, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        // Per-tenant ledger partition — see PointsExpirationJobE2ETests.
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS ledger_entries_t1 PARTITION OF ledger_entries FOR VALUES IN ('t1')");
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task<Guid> AddAccountTypeAsync(string type, string config, bool isTierQualifying = false)
    {
        var id = Guid.NewGuid();
        _db.AccountTypes.Add(new AccountTypeEntity
        {
            Id = id, TenantId = _tenantId, ProgramId = _programId, Type = type, Name = $"{type}-{id:N}"[..20],
            Config = config, IsTierQualifying = isTierQualifying, CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task<CustomerAccount> AddCustomerAccountAsync(string contactKey, Guid accountTypeId, decimal earned, int earnedDaysAgo)
    {
        var account = new CustomerAccount
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ContactKey = contactKey, AccountTypeId = accountTypeId,
            Balance = earned, UpdatedAt = DateTime.UtcNow
        };
        _db.CustomerAccounts.Add(account);
        await _db.SaveChangesAsync();

        _db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(), TenantId = "t1", CustomerAccountId = account.Id, ContactKey = contactKey,
            Delta = earned, Reason = LedgerReason.Earn, SourceEventId = "seed",
            IdempotencyKey = $"seed:{contactKey}", CreatedAt = DateTime.UtcNow.AddDays(-earnedDaysAgo)
        });
        await _db.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task Expiring_warning_is_driven_by_the_account_type_warning_days()
    {
        // expiration 30, warning 7 → points earned more than 23 days ago are "expiring soon".
        var warned = await AddAccountTypeAsync("POINTS", """{"decimals": 0, "expiration_days": 30, "warning_days": 7}""");
        var silent = await AddAccountTypeAsync("POINTS", """{"decimals": 0, "expiration_days": 30}""");
        await AddCustomerAccountAsync("warn_me", warned, 500m, 25);
        await AddCustomerAccountAsync("no_warning", silent, 500m, 25);

        await new PointsExpiringDetectorJob(_db, NullLogger<PointsExpiringDetectorJob>.Instance).RunAsync();

        var expiring = await _db.OutboxEvents.AsNoTracking()
            .Where(o => o.EventType == "loyalty.points.expiring").Select(o => o.ContactKey).ToListAsync();
        expiring.Should().Equal("warn_me");
    }

    [Fact]
    public async Task Tier_context_resolves_the_flagged_wallet_not_the_program_column()
    {
        var qualifying = await AddAccountTypeAsync("POINTS", """{"decimals": 0}""", isTierQualifying: true);
        await AddAccountTypeAsync("POINTS", """{"decimals": 0}""");

        var loader = new TierContextLoader(_db, new TenantSlugResolver(_db, new TenantSlugCache()));
        var context = await loader.LoadAsync("t1", _programId, "evt-1",
            new EvaluationEvent { EventType = "order.created", ContactKey = "c1", Amount = 1m },
            Array.Empty<ConditionTree?>(), Array.Empty<List<ConditionClause>?>(), CancellationToken.None);

        context.QualifyingAccountTypeId.Should().Be(qualifying);
    }

    [Fact]
    public async Task Tier_downgrade_job_only_downgrades_accounts_on_the_tier_qualifying_wallet()
    {
        var qualifying = await AddAccountTypeAsync("POINTS", """{"decimals": 0}""", isTierQualifying: true);
        var other = await AddAccountTypeAsync("POINTS", """{"decimals": 0}""");

        var silver = new TierDefinition { Id = Guid.NewGuid(), TenantId = _tenantId, ProgramId = _programId, Name = "silver", DisplayName = "Silver", MinPoints = 0m, QualifyingDays = 30, SortOrder = 1, CreatedAt = DateTime.UtcNow };
        var gold = new TierDefinition { Id = Guid.NewGuid(), TenantId = _tenantId, ProgramId = _programId, Name = "gold", DisplayName = "Gold", MinPoints = 1000m, QualifyingDays = 30, SortOrder = 2, CreatedAt = DateTime.UtcNow };
        _db.TierDefinitions.AddRange(silver, gold);
        await _db.SaveChangesAsync();

        // Both accounts sit in Gold with an expired period and no qualifying points in it.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var account in new[]
                 {
                     await AddCustomerAccountAsync("on_qualifying", qualifying, 10m, 45),
                     await AddCustomerAccountAsync("on_other", other, 10m, 45)
                 })
        {
            account.TierId = gold.Id;
            account.TierPeriodStart = today.AddDays(-40);
            account.TierExpiresAt = today.AddDays(-1);
        }
        await _db.SaveChangesAsync();

        await new TierDowngradeJob(_db, NullLogger<TierDowngradeJob>.Instance).RunAsync();

        var tiers = await _db.CustomerAccounts.AsNoTracking()
            .ToDictionaryAsync(a => a.ContactKey, a => a.TierId);
        tiers["on_qualifying"].Should().Be(silver.Id);
        tiers["on_other"].Should().Be(gold.Id);
    }
}

// 1.3.CL Phase 1 migration backfill (AccountTypeConfigCl13), exercised against a database that
// holds pre-1.3.CL data: migrate to the migration before it, seed the old shape, migrate on.
[Trait("Category", "E2E")]
public sealed class AccountTypeConfigCl13MigrationE2ETests : IAsyncLifetime
{
    private const string MigrationBefore = "20260921161222_CustomerBirthdayCr10";
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private LoyaltyDbContext _db = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _db = new LoyaltyDbContext(new DbContextOptionsBuilder<LoyaltyDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Backfill_moves_program_settings_onto_the_account_types_and_strips_cash_expiry()
    {
        await _db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(MigrationBefore);

        Guid tenant = Guid.NewGuid(), program = Guid.NewGuid(), otherProgram = Guid.NewGuid();
        Guid points = Guid.NewGuid(), shortExpiry = Guid.NewGuid(), cash = Guid.NewGuid(), foreign = Guid.NewGuid();

        // Raw SQL: the current entity model already has the columns this backfill adds.
        await _db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO tenants (id, slug, name, status, created_at) VALUES ({{tenant}}, 't1', 't1', 'active', now());
            INSERT INTO programs (id, tenant_id, name, status, created_at, warning_days)
                VALUES ({{program}}, {{tenant}}, 'p1', 'active', now(), 10),
                       ({{otherProgram}}, {{tenant}}, 'p2', 'active', now(), NULL);
            INSERT INTO account_types (id, tenant_id, program_id, type, name, config, created_at) VALUES
                ({{points}},      {{tenant}}, {{program}},      'POINTS', 'Points', '{"decimals": 0, "expiration_days": 180}', now()),
                ({{shortExpiry}}, {{tenant}}, {{program}},      'POINTS', 'Short',  '{"decimals": 0, "expiration_days": 5}', now()),
                ({{cash}},        {{tenant}}, {{program}},      'CASH',   'Cash',   '{"currency": "SAR", "decimals": 2, "expiration_days": 90}', now()),
                ({{foreign}},     {{tenant}}, {{otherProgram}}, 'POINTS', 'Other',  '{"decimals": 0}', now());
            UPDATE programs SET qualifying_account_type_id = {{points}} WHERE id = {{program}};
            UPDATE programs SET qualifying_account_type_id = {{points}} WHERE id = {{otherProgram}};
            """);

        await _db.Database.MigrateAsync();

        var rows = await _db.AccountTypes.AsNoTracking().ToDictionaryAsync(a => a.Id);
        rows[points].IsTierQualifying.Should().BeTrue();
        rows[foreign].IsTierQualifying.Should().BeFalse("a cross-program pointer never marks another program's wallet");
        rows[points].Config.Should().Contain("\"warning_days\": 10");
        rows[shortExpiry].Config.Should().NotContain("warning_days", "10 >= expiration_days 5 was never a valid warning");
        rows[cash].Config.Should().NotContain("expiration_days");
        rows[cash].Config.Should().Contain("\"currency\": \"SAR\"");
    }

    // 1.3.CL item 5 (RuleStackingRetirementCl13): clearing a rule's group / multiplier mode is an
    // edit, so the pre-change state is archived to rule_versions and current_version is bumped.
    [Fact]
    public async Task Stacking_retirement_archives_the_old_version_before_clearing_the_fields()
    {
        await _db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(MigrationBefore);

        Guid tenant = Guid.NewGuid(), program = Guid.NewGuid(), points = Guid.NewGuid();
        Guid grouped = Guid.NewGuid(), multiplier = Guid.NewGuid(), untouched = Guid.NewGuid();
        await _db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO tenants (id, slug, name, status, created_at) VALUES ({{tenant}}, 't1', 't1', 'active', now());
            INSERT INTO programs (id, tenant_id, name, status, created_at) VALUES ({{program}}, {{tenant}}, 'p1', 'active', now());
            INSERT INTO account_types (id, tenant_id, program_id, type, name, config, created_at)
                VALUES ({{points}}, {{tenant}}, {{program}}, 'POINTS', 'Points', '{"decimals": 0}', now());
            INSERT INTO rules (id, tenant_id, program_id, name, type, trigger, calculation, target_account_type_id,
                               priority, stackable, exclusivity_group, stack_mode, status, current_version, created_at, updated_at) VALUES
                ({{grouped}},    {{tenant}}, {{program}}, 'grouped',    'SpendRule', 'remittance', '{"factor": 1}', {{points}}, 10, false, 'earn-rate', 'Additive',   'active', 3, now(), now()),
                ({{multiplier}}, {{tenant}}, {{program}}, 'multiplier', 'SpendRule', 'remittance', '{"factor": 2}', {{points}}, 5,  true,  NULL,        'Multiplier', 'active', 1, now(), now()),
                ({{untouched}},  {{tenant}}, {{program}}, 'plain',      'SpendRule', 'remittance', '{"factor": 1}', {{points}}, 1,  true,  NULL,        'Additive',   'active', 1, now(), now());
            """);

        await _db.Database.MigrateAsync();

        var rules = await _db.Rules.AsNoTracking().ToDictionaryAsync(r => r.Id);
        rules[grouped].ExclusivityGroup.Should().BeNull();
        rules[grouped].CurrentVersion.Should().Be(4);
        rules[multiplier].StackMode.Should().Be(RuleStackMode.Additive);
        rules[multiplier].CurrentVersion.Should().Be(2);
        rules[untouched].CurrentVersion.Should().Be(1, "rules without retired fields are not re-versioned");

        var archived = await _db.RuleVersions.AsNoTracking().ToListAsync();
        archived.Should().HaveCount(2);
        archived.Should().ContainSingle(v => v.RuleId == grouped && v.VersionNumber == 3 && v.ExclusivityGroup == "earn-rate");
        archived.Should().ContainSingle(v => v.RuleId == multiplier && v.VersionNumber == 1 && v.StackMode == RuleStackMode.Multiplier);
    }
}
