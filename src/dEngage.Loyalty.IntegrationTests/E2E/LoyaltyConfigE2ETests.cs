using dEngage.Loyalty.Schema;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.E2E;

// Platform alignment D4: loyalty_config and its reader, on real Postgres (migration, jsonb,
// partial unique indexes). Requires Docker.
[Trait("Category", "E2E")]
public sealed class LoyaltyConfigE2ETests : IAsyncLifetime
{
    private const string Module = "test.module";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private LoyaltyDbContext _db = default!;
    private Guid _tenantA;
    private Guid _tenantB;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _db = new LoyaltyDbContext(new DbContextOptionsBuilder<LoyaltyDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
        await _db.Database.MigrateAsync();

        _tenantA = Guid.NewGuid();
        _tenantB = Guid.NewGuid();
        _db.Tenants.AddRange(
            new Tenant { Id = _tenantA, Slug = "tenant_a", Name = "A", Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow },
            new Tenant { Id = _tenantB, Slug = "tenant_b", Name = "B", Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task A_tenant_row_wins_over_the_global_row_which_wins_over_the_default()
    {
        await AddAsync("Limit", null, value: "100");
        await AddAsync("Limit", _tenantA, value: "250");

        var reader = Reader();
        (await reader.GetAsync(Module, "Limit", 7, _tenantA, default)).Should().Be(250);
        (await reader.GetAsync(Module, "Limit", 7, _tenantB, default)).Should().Be(100, "tenant B has no row of its own");
        (await reader.GetAsync(Module, "Limit", 7, null, default)).Should().Be(100);
        (await reader.GetAsync(Module, "Missing", 7, _tenantA, default)).Should().Be(7, "no row means the code default");
    }

    [Fact]
    public async Task Values_are_read_as_the_callers_type()
    {
        await AddAsync("List", null, valueJson: """["a","b"]""");
        await AddAsync("Numbers", null, valueJson: "[1, 5, 30]");
        await AddAsync("Flag", null, value: "true");
        await AddAsync("Name", null, value: "plain text");
        await AddAsync("JsonName", null, valueJson: "\"from json\"");
        await AddAsync("Blank", null, value: "  ");

        var reader = Reader();
        (await reader.GetAsync<string[]>(Module, "List", [], null, default)).Should().Equal("a", "b");
        (await reader.GetAsync<int[]>(Module, "Numbers", [], null, default)).Should().Equal(1, 5, 30);
        (await reader.GetAsync(Module, "Flag", false, null, default)).Should().BeTrue();
        (await reader.GetAsync(Module, "Name", "", null, default)).Should().Be("plain text");
        (await reader.GetAsync(Module, "JsonName", "", null, default)).Should().Be("from json");
        (await reader.GetAsync(Module, "Blank", 3, null, default)).Should().Be(3, "an empty value counts as unset");
    }

    [Fact]
    public async Task A_value_that_cant_be_read_fails_loudly_instead_of_using_the_default()
    {
        await AddAsync("Limit", _tenantA, value: "lots");

        var act = () => Reader().GetAsync(Module, "Limit", 7, _tenantA, default);

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
            await AddAsync("ApiSecret", null, value: SecretCrypto.Encrypt("s3cret", Convert.FromBase64String(key)), isSecret: true);

            (await Reader().GetAsync(Module, "ApiSecret", "", null, default)).Should().Be("s3cret");

            await AddAsync("PlainSecret", null, value: "not-encrypted", isSecret: true, module: "test.other");
            var act = () => Reader().GetAsync("test.other", "PlainSecret", "", null, default);
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
        await AddAsync("Limit", null, value: "100");
        var clock = new ManualClock();
        var cache = new LoyaltyConfigCache(clock);

        (await Reader(cache).GetAsync(Module, "Limit", 7, null, default)).Should().Be(100);

        await _db.LoyaltyConfig.Where(x => x.ParameterName == "Limit")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Value, "200"));

        clock.Advance(LoyaltyConfigCache.Ttl - TimeSpan.FromSeconds(1));
        (await Reader(cache).GetAsync(Module, "Limit", 7, null, default)).Should().Be(100, "still within the TTL");

        clock.Advance(TimeSpan.FromSeconds(2));
        (await Reader(cache).GetAsync(Module, "Limit", 7, null, default)).Should().Be(200);
    }

    [Fact]
    public async Task One_global_row_and_one_row_per_tenant_for_each_setting()
    {
        await AddAsync("Unique", null, value: "1");
        await AddAsync("Unique", _tenantA, value: "2");
        await AddAsync("Unique", _tenantB, value: "3");

        var secondGlobal = () => AddAsync("Unique", null, value: "4");
        await secondGlobal.Should().ThrowAsync<DbUpdateException>("ux_loyalty_config_global");

        var secondForTenant = () => AddAsync("Unique", _tenantA, value: "5");
        await secondForTenant.Should().ThrowAsync<DbUpdateException>("ux_loyalty_config_tenant");
    }

    private LoyaltyConfigReader Reader(LoyaltyConfigCache? cache = null) =>
        new(_db, cache ?? new LoyaltyConfigCache(), new SecretCryptoProtector());

    private async Task AddAsync(string parameter, Guid? tenantId, string? value = null, string? valueJson = null,
        bool isSecret = false, string module = Module)
    {
        // A fresh context per insert, so a failed insert doesn't leave a pending entity behind.
        await using var db = new LoyaltyDbContext(new DbContextOptionsBuilder<LoyaltyDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
        db.LoyaltyConfig.Add(new LoyaltyConfigEntry
        {
            ModuleName = module, ParameterName = parameter, TenantId = tenantId,
            Value = value, ValueJson = valueJson, IsSecret = isSecret, UpdatedAt = DateTime.UtcNow
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
