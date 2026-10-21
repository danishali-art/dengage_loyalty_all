using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Platform;
using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.E2E;

// Platform alignment D4 on real Postgres through the migrations. The tables are the only source:
// global settings in loyalty_config, tenant settings in loyalty_tenant_config with no global value.
// Requires Docker.
[Trait("Category", "E2E")]
public sealed class LoyaltyConfigE2ETests : IAsyncLifetime
{
    private const string Module = "test.module";

    private static readonly string RateLimit = LoyaltyConfigKeys.Api.RateLimitEventIngestionPerMinute;
    private static readonly string ExtraMasked = LoyaltyConfigKeys.Api.PiiExtraMaskedFields;
    private static readonly string ApiModule = LoyaltyConfigKeys.Api.Module;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private LoyaltyDbContext _db = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _db = NewContext();
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    // ── Global settings ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_global_row_is_used_and_a_missing_or_blank_row_fails()
    {
        await AddGlobalAsync("Limit", value: "250");
        await AddGlobalAsync("Blank", value: "  ");

        var reader = Reader();
        (await reader.GetAsync<int>(Module, "Limit", default)).Should().Be(250);

        var missing = () => reader.GetAsync<int>(Module, "Missing", default);
        await missing.Should().ThrowAsync<InvalidOperationException>().WithMessage("loyalty_config_missing: no value for test.module/Missing*");

        var blank = () => reader.GetAsync<int>(Module, "Blank", default);
        await blank.Should().ThrowAsync<InvalidOperationException>().WithMessage("loyalty_config_missing*");
    }

    [Fact]
    public async Task Values_are_read_as_the_callers_type()
    {
        await AddGlobalAsync("List", valueJson: """["a","b"]""");
        await AddGlobalAsync("Numbers", valueJson: "[1, 5, 30]");
        await AddGlobalAsync("Flag", value: "true");
        await AddGlobalAsync("Name", value: "plain text");
        await AddGlobalAsync("JsonName", valueJson: "\"from json\"");

        var reader = Reader();
        (await reader.GetAsync<string[]>(Module, "List", default)).Should().Equal("a", "b");
        (await reader.GetAsync<int[]>(Module, "Numbers", default)).Should().Equal(1, 5, 30);
        (await reader.GetAsync<bool>(Module, "Flag", default)).Should().BeTrue();
        (await reader.GetAsync<string>(Module, "Name", default)).Should().Be("plain text");
        (await reader.GetAsync<string>(Module, "JsonName", default)).Should().Be("from json");
    }

    [Fact]
    public async Task A_value_that_cant_be_read_fails_loudly()
    {
        await AddGlobalAsync("Limit", value: "lots");

        var act = () => Reader().GetAsync<int>(Module, "Limit", default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("loyalty_config_invalid: test.module/Limit*");
    }

    [Fact]
    public async Task Secret_rows_are_decrypted_and_a_plaintext_secret_is_refused()
    {
        var saved = Environment.GetEnvironmentVariable(SecretCrypto.KeyEnvVar);
        var savedFile = Environment.GetEnvironmentVariable(SecretCrypto.KeyFileEnvVar);
        try
        {
            var key = SecretCrypto.GenerateKey();
            Environment.SetEnvironmentVariable(SecretCrypto.KeyFileEnvVar, null);
            Environment.SetEnvironmentVariable(SecretCrypto.KeyEnvVar, key);
            await AddGlobalAsync("ApiSecret", value: SecretCrypto.Encrypt("s3cret", Convert.FromBase64String(key)), isSecret: true);

            (await Reader().GetAsync<string>(Module, "ApiSecret", default)).Should().Be("s3cret");

            await AddGlobalAsync("PlainSecret", value: "not-encrypted", isSecret: true, module: "test.other");
            var act = () => Reader().GetAsync<string>("test.other", "PlainSecret", default);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("secret_not_encrypted*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(SecretCrypto.KeyEnvVar, saved);
            Environment.SetEnvironmentVariable(SecretCrypto.KeyFileEnvVar, savedFile);
        }
    }

    [Fact]
    public async Task A_change_is_seen_once_the_cached_module_is_older_than_the_ttl()
    {
        await AddGlobalAsync("Limit", value: "100");
        var clock = new ManualClock();
        var cache = new LoyaltyConfigCache(clock);

        (await Reader(cache).GetAsync<int>(Module, "Limit", default)).Should().Be(100);

        await _db.LoyaltyConfig.Where(x => x.ModuleName == Module && x.ParameterName == "Limit")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Value, "200"));

        clock.Advance(LoyaltyConfigCache.Ttl - TimeSpan.FromSeconds(1));
        (await Reader(cache).GetAsync<int>(Module, "Limit", default)).Should().Be(100, "still within the TTL");

        clock.Advance(TimeSpan.FromSeconds(2));
        (await Reader(cache).GetAsync<int>(Module, "Limit", default)).Should().Be(200);
    }

    [Fact]
    public async Task There_is_exactly_one_global_row_per_setting()
    {
        await AddGlobalAsync("Unique", value: "1");

        var second = () => AddGlobalAsync("Unique", value: "2");

        await second.Should().ThrowAsync<DbUpdateException>("ux_loyalty_config_key");
    }

    [Fact]
    public async Task The_migrations_seed_exactly_the_global_settings_in_LoyaltyConfigDefaults()
    {
        var rows = await _db.LoyaltyConfig.AsNoTracking().Where(x => x.ModuleName.StartsWith("loyalty.")).ToListAsync();

        rows.Select(r => (r.ModuleName, r.ParameterName, r.Value, Json: Normalize(r.ValueJson)))
            .Should().BeEquivalentTo(LoyaltyConfigDefaults.Global.Select(d => (d.Module, d.Parameter, d.Value, Json: Normalize(d.ValueJson))));
    }

    // ── Tenant settings ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_tenant_setting_comes_only_from_that_tenants_row()
    {
        var a = await AddTenantAsync("tenant_a");
        var b = await AddTenantAsync("tenant_b");
        await AddTenantSettingAsync(a, RateLimit, value: "2000");
        await AddTenantSettingAsync(a, ExtraMasked, valueJson: """["iqama_no"]""");

        var reader = Reader();
        (await reader.GetForTenantAsync<int>(ApiModule, RateLimit, a, default)).Should().Be(2000);
        (await reader.GetForTenantAsync<string[]>(ApiModule, ExtraMasked, a, default)).Should().Equal("iqama_no");

        // Tenant B has no rows and there is no global value to fall back to.
        var missing = () => reader.GetForTenantAsync<int>(ApiModule, RateLimit, b, default);
        await missing.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"loyalty_config_missing: no value for loyalty.api/{RateLimit} for tenant {b}*");
    }

    [Fact]
    public async Task Each_kind_of_setting_can_only_be_read_its_own_way()
    {
        var reader = Reader();

        var tenantKeyAsGlobal = () => reader.GetAsync<int>(ApiModule, RateLimit, default);
        await tenantKeyAsGlobal.Should().ThrowAsync<InvalidOperationException>().WithMessage("*is a tenant setting*");

        var globalKeyAsTenant = () => reader.GetForTenantAsync<int>(
            LoyaltyConfigKeys.Jobs.Module, LoyaltyConfigKeys.Jobs.OutboxBatchSize, Guid.NewGuid(), default);
        await globalKeyAsTenant.Should().ThrowAsync<InvalidOperationException>().WithMessage("*is a global setting*");
    }

    [Fact]
    public async Task The_database_keeps_tenant_settings_and_global_settings_apart()
    {
        var a = await AddTenantAsync("tenant_a");

        var globalRowForTenantSetting = () => AddGlobalAsync(RateLimit, value: "120", module: ApiModule);
        await globalRowForTenantSetting.Should().ThrowAsync<DbUpdateException>("ck_loyalty_config_global_settings");

        var tenantRowForGlobalSetting = () => AddTenantSettingAsync(a, LoyaltyConfigKeys.Jobs.OutboxBatchSize,
            value: "10", module: LoyaltyConfigKeys.Jobs.Module);
        await tenantRowForGlobalSetting.Should().ThrowAsync<DbUpdateException>("ck_loyalty_tenant_config_tenant_settings");

        await AddTenantSettingAsync(a, RateLimit, value: "1");
        var secondRow = () => AddTenantSettingAsync(a, RateLimit, value: "2");
        await secondRow.Should().ThrowAsync<DbUpdateException>("ux_loyalty_tenant_config_key");
    }

    [Fact]
    public async Task Deleting_a_tenant_deletes_its_settings()
    {
        var a = await AddTenantAsync("tenant_a");
        await AddTenantSettingAsync(a, RateLimit, value: "50");

        await _db.Tenants.Where(t => t.Id == a).ExecuteDeleteAsync();

        (await _db.LoyaltyTenantConfig.AnyAsync(x => x.TenantId == a)).Should().BeFalse();
    }

    [Fact]
    public async Task Creating_a_tenant_through_the_platform_writes_its_starting_settings()
    {
        // Built by hand (no test-host interceptor), so this fails if CreateTenantAsync forgets them.
        await using var db = NewContext();
        var passwords = new PasswordService();
        var platform = new PlatformAppService(db, new ApiKeyGenerator(passwords), passwords,
            new TenantSlugResolver(db, new TenantSlugCache()));

        await platform.CreateTenantAsync(new CreateTenantRequest("fresh_tenant", "Fresh"), default);

        var tenantId = await _db.Tenants.Where(t => t.Slug == "fresh_tenant").Select(t => t.Id).SingleAsync();
        var rows = await _db.LoyaltyTenantConfig.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync();
        rows.Select(r => (r.ModuleName, r.ParameterName, r.Value, Json: Normalize(r.ValueJson)))
            .Should().BeEquivalentTo(LoyaltyConfigDefaults.NewTenant.Select(d => (d.Module, d.Parameter, d.Value, Json: Normalize(d.ValueJson))));
        (await Reader().GetForTenantAsync<int>(ApiModule, RateLimit, tenantId, default)).Should().Be(120);
    }

    // ── Migration behaviour ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Existing_tenants_get_their_settings_from_the_previous_global_values()
    {
        var migrator = _db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync("20261020143545_LoyaltyConfigGlobalOnlyAlignD4");

        // State before the tenant table: one global value, changed by this environment.
        Guid existing;
        await using (var before = NewContext())
        {
            await before.LoyaltyConfig.Where(x => x.ParameterName == RateLimit)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Value, "300"));
            existing = Guid.NewGuid();
            await before.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO tenants (id, slug, name, status, created_at) VALUES ({existing}, 'existing', 'Existing', 'active', now())");
        }

        await migrator.MigrateAsync();

        await using var after = NewContext();
        var reader = new LoyaltyConfigReader(after, new LoyaltyConfigCache(), new SecretCryptoProtector());
        (await reader.GetForTenantAsync<int>(ApiModule, RateLimit, existing, default)).Should().Be(300, "the environment's value carries over");
        (await reader.GetForTenantAsync<string[]>(ApiModule, ExtraMasked, existing, default)).Should().BeEmpty();
        (await after.LoyaltyConfig.AnyAsync(x => x.ParameterName == RateLimit || x.ParameterName == ExtraMasked))
            .Should().BeFalse("tenant settings have no global row");
    }

    [Fact]
    public async Task Rolling_back_keeps_changed_values_and_reapplying_never_overwrites_them()
    {
        await _db.LoyaltyConfig
            .Where(x => x.ParameterName == LoyaltyConfigKeys.Jobs.OutboxBatchSize)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Value, "250").SetProperty(x => x.UpdatedBy, "ops"));

        var migrator = _db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync("20261020135214_LoyaltyConfigAlignD4");

        await using (var check = NewContext())
        {
            var left = await check.LoyaltyConfig.AsNoTracking().Where(x => x.ModuleName.StartsWith("loyalty.")).ToListAsync();
            left.Should().ContainSingle().Which.Value.Should().Be("250", "only rows still as seeded are removed");
        }

        await migrator.MigrateAsync();

        await using var after = NewContext();
        (await after.LoyaltyConfig.CountAsync(x => x.ModuleName.StartsWith("loyalty."))).Should().Be(LoyaltyConfigDefaults.Global.Count);
        (await after.LoyaltyConfig.AsNoTracking().SingleAsync(x => x.ParameterName == LoyaltyConfigKeys.Jobs.OutboxBatchSize))
            .Value.Should().Be("250", "ON CONFLICT DO NOTHING keeps the environment's value");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static string? Normalize(string? json) =>
        json is null ? null : System.Text.Json.JsonDocument.Parse(json).RootElement.GetRawText().Replace(" ", "");

    private LoyaltyDbContext NewContext() =>
        new(new DbContextOptionsBuilder<LoyaltyDbContext>().UseNpgsql(_container.GetConnectionString()).Options);

    private LoyaltyConfigReader Reader(LoyaltyConfigCache? cache = null) =>
        new(_db, cache ?? new LoyaltyConfigCache(), new SecretCryptoProtector());

    private async Task<Guid> AddTenantAsync(string slug)
    {
        await using var db = NewContext();
        var id = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = id, Slug = slug, Name = slug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    // A fresh context per insert, so a failed insert doesn't leave a pending entity behind.
    private async Task AddGlobalAsync(string parameter, string? value = null, string? valueJson = null,
        bool isSecret = false, string module = Module)
    {
        await using var db = NewContext();
        db.LoyaltyConfig.Add(new LoyaltyConfigEntry
        {
            ModuleName = module, ParameterName = parameter,
            Value = value, ValueJson = valueJson, IsSecret = isSecret, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task AddTenantSettingAsync(Guid tenantId, string parameter, string? value = null, string? valueJson = null,
        string? module = null)
    {
        await using var db = NewContext();
        db.LoyaltyTenantConfig.Add(new LoyaltyTenantConfigEntry
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ModuleName = module ?? ApiModule, ParameterName = parameter,
            Value = value, ValueJson = valueJson, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
