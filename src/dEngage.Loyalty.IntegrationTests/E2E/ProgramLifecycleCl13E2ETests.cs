using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.E2E;

// 1.3.CL end to end over HTTP against a real Postgres (migrated with every 1.3.CL migration):
// create draft → account types (tier flag, warning days, CASH currency) → publish → activate →
// edit → re-publish, checking the jsonb snapshot rows the Sqlite suites can't prove.
[Trait("Category", "E2E")]
public sealed class ProgramLifecycleCl13E2ETests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private PostgresWebApplicationFactory _factory = default!;
    private HttpClient _client = default!;
    private const string TenantSlug = "t1";
    private const string ProgramsUrl = $"/api/v1/tenants/{TenantSlug}/programs";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _factory = new PostgresWebApplicationFactory(_container.GetConnectionString());
        await _factory.MigrateDbAsync();
        await using (var db = _factory.CreateDbContext())
        {
            db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Slug = TenantSlug, Name = TenantSlug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        _client = _factory.CreateClient();
        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static StringContent Json(object body) => new(
        JsonSerializer.Serialize(body, JsonConventions.Options), System.Text.Encoding.UTF8, "application/json");

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await call;
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(JsonConventions.Options))!;
    }

    [Fact]
    public async Task Draft_to_published_lifecycle_writes_publish_snapshots_on_postgres()
    {
        var program = await OkAsync<ProgramResponse>(_client.PostAsync(ProgramsUrl, Json(new CreateProgramRequest("Lifecycle", null))), HttpStatusCode.Created);
        program.PublicationStatus.Should().Be(ProgramPublicationStatus.Draft);
        var programUrl = $"{ProgramsUrl}/{program.Id}";

        await OkAsync<JsonElement>(_client.PostAsync($"{programUrl}/account-types", Json(new
        {
            type = "POINTS", name = "Stars", isTierQualifying = true,
            config = new Dictionary<string, object> { ["decimals"] = 2, ["expiration_days"] = 180, ["warning_days"] = 14 }
        })), HttpStatusCode.Created);
        await OkAsync<JsonElement>(_client.PostAsync($"{programUrl}/account-types", Json(new
        {
            type = "CASH", name = "Wallet", config = new { currency = "SAR", decimals = 2 }
        })), HttpStatusCode.Created);

        // Publish → v1, then the toggle works.
        var v1 = await OkAsync<ProgramResponse>(_client.PostAsync($"{programUrl}/publish", null));
        v1.PublishedVersion.Should().Be(1);
        v1.QualifyingAccountTypeId.Should().NotBeNull("the deprecated field is derived from the tier flag");
        (await OkAsync<ProgramResponse>(_client.PatchAsync(programUrl, Json(new UpdateProgramRequest(null, null, ProgramStatus.Active)))))
            .Status.Should().Be(ProgramStatus.Active);

        // Edit nested config → unpublished changes → v2.
        await OkAsync<JsonElement>(_client.PostAsync($"{programUrl}/account-types", Json(new
        {
            type = "CASH", name = "Wallet USD", config = new { currency = "USD", decimals = 2 }
        })), HttpStatusCode.Created);
        var v2 = await OkAsync<ProgramResponse>(_client.PostAsync($"{programUrl}/publish", null));
        v2.PublishedVersion.Should().Be(2);
        v2.HasUnpublishedChanges.Should().BeFalse();

        await using var db = _factory.CreateDbContext();
        var snapshots = await db.ConfigVersions.AsNoTracking()
            .Where(v => v.EntityId == program.Id && v.EntityType == ProgramsAppService.PublicationEntityType)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();
        snapshots.Select(s => s.VersionNumber).Should().Equal(1, 2);
        snapshots.Should().OnlyContain(s => s.ChangeType == ConfigChangeType.Published);

        var latest = JsonDocument.Parse(snapshots[1].Snapshot).RootElement;
        latest.GetProperty("AccountTypes").GetArrayLength().Should().Be(3);
        latest.GetProperty("Program").GetProperty("Status").GetString().Should().Be(ProgramStatus.Active);
        // The POINTS wallet's config round-trips as JSON, warning_days included.
        latest.GetProperty("AccountTypes").EnumerateArray()
            .Single(a => a.GetProperty("Type").GetString() == "POINTS")
            .GetProperty("Config").GetProperty("warning_days").GetInt32().Should().Be(14);
    }
}

// ProgramPublicationCl13 backfill against pre-1.3.CL data: programs that already exist are live
// configuration, so they come out 'published' with nothing pending.
[Trait("Category", "E2E")]
public sealed class ProgramPublicationCl13MigrationE2ETests : IAsyncLifetime
{
    private const string MigrationBefore = "20260928190845_RuleStackingRetirementCl13";
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private dEngage.Loyalty.Schema.LoyaltyDbContext _db = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _db = new dEngage.Loyalty.Schema.LoyaltyDbContext(new DbContextOptionsBuilder<dEngage.Loyalty.Schema.LoyaltyDbContext>()
            .UseNpgsql(_container.GetConnectionString()).Options);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Existing_programs_are_backfilled_as_published_with_nothing_pending()
    {
        await Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
            .GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(_db).MigrateAsync(MigrationBefore);

        Guid tenant = Guid.NewGuid(), active = Guid.NewGuid(), inactive = Guid.NewGuid();
        await _db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO tenants (id, slug, name, status, created_at) VALUES ({{tenant}}, 't1', 't1', 'active', now());
            INSERT INTO programs (id, tenant_id, name, status, created_at) VALUES
                ({{active}}, {{tenant}}, 'live', 'active', now()),
                ({{inactive}}, {{tenant}}, 'paused', 'inactive', now());
            """);

        await _db.Database.MigrateAsync();

        var programs = await _db.Programs.AsNoTracking().ToListAsync();
        programs.Should().HaveCount(2).And.OnlyContain(p =>
            p.PublicationStatus == ProgramPublicationStatus.Published && !p.HasUnpublishedChanges && p.PublishedVersion == null);
        programs.Single(p => p.Id == active).Status.Should().Be(ProgramStatus.Active, "the backfill never changes whether a program runs");
    }
}
