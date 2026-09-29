using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using dEngage.Loyalty.Api.AccountTypes;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Api;

// 1.3.CL Phase 1 (docs/scope-changes/2026-09-28-program-account-type-changes.md §3.1–3.5) over
// the real Nancy pipeline: account type config validation (warning days, CASH currency and
// expiry, decimals), the tier-qualifying flag and its locks, and the Program fields that moved.
public sealed class AccountTypeChangesCl13ApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string TenantSlug = "cl13-at";
    private const string OtherTenantSlug = "cl13-at-other";

    public AccountTypeChangesCl13ApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureDbCreatedAsync();
        await using (var db = _factory.CreateDbContext())
        {
            foreach (var slug in new[] { TenantSlug, OtherTenantSlug })
                if (!db.Tenants.Any(t => t.Slug == slug))
                    db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Slug = slug, Name = slug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static StringContent Json(object body) => new(
        System.Text.Json.JsonSerializer.Serialize(body, JsonConventions.Options), System.Text.Encoding.UTF8, "application/json");

    private string ProgramsUrl => $"/api/v1/tenants/{TenantSlug}/programs";
    private string AccountTypesUrl(Guid programId) => $"{ProgramsUrl}/{programId}/account-types";

    private async Task<Guid> CreateProgramAsync()
    {
        var response = await _client.PostAsync(ProgramsUrl, Json(new CreateProgramRequest($"cl13-{Guid.NewGuid():N}", null)));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options))!.Id;
    }

    private Task<HttpResponseMessage> PostAccountTypeAsync(Guid programId, string type, Dictionary<string, object> config, bool? isTierQualifying = null) =>
        _client.PostAsync(AccountTypesUrl(programId), Json(new
        {
            type,
            name = $"{type}-{Guid.NewGuid():N}"[..20],
            config,
            isTierQualifying
        }));

    private static async Task<AccountTypeResponse> ReadAccountTypeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<AccountTypeResponse>(JsonConventions.Options))!;

    // ── Item 4: CASH currency ────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("SAR", HttpStatusCode.Created)]
    [InlineData("AED", HttpStatusCode.Created)]
    [InlineData("XYZ", HttpStatusCode.BadRequest)]
    [InlineData("sar", HttpStatusCode.BadRequest)]
    public async Task Cash_currency_must_be_on_the_supported_list(string currency, HttpStatusCode expected)
    {
        var programId = await CreateProgramAsync();
        var response = await PostAccountTypeAsync(programId, "CASH", new() { ["currency"] = currency, ["decimals"] = 2 });
        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Cash_currency_cannot_change_after_creation_but_other_config_can()
    {
        var programId = await CreateProgramAsync();
        var created = await ReadAccountTypeAsync(await PostAccountTypeAsync(programId, "CASH", new() { ["currency"] = "SAR", ["decimals"] = 2 }));

        var changeCurrency = await _client.PatchAsync($"{AccountTypesUrl(programId)}/{created.Id}",
            Json(new { config = new Dictionary<string, object> { ["currency"] = "USD", ["decimals"] = 2 } }));
        changeCurrency.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var changeDecimals = await _client.PatchAsync($"{AccountTypesUrl(programId)}/{created.Id}",
            Json(new { config = new Dictionary<string, object> { ["currency"] = "SAR", ["decimals"] = 3 } }));
        changeDecimals.StatusCode.Should().Be(HttpStatusCode.OK, await changeDecimals.Content.ReadAsStringAsync());
    }

    // ── Item 3: CASH never expires ───────────────────────────────────────────────────────
    [Fact]
    public async Task Cash_config_with_expiration_days_is_rejected()
    {
        var programId = await CreateProgramAsync();
        var response = await PostAccountTypeAsync(programId, "CASH",
            new() { ["currency"] = "SAR", ["decimals"] = 2, ["expiration_days"] = 90 });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Items 1 + 2: POINTS warning days and decimals ─────────────────────────────────────
    public static TheoryData<Dictionary<string, object>> InvalidPointsConfigs => new()
    {
        new() { ["decimals"] = 0, ["expiration_days"] = 30, ["warning_days"] = 30 },  // warning == expiry
        new() { ["decimals"] = 0, ["expiration_days"] = 30, ["warning_days"] = 45 },  // warning > expiry
        new() { ["decimals"] = 0, ["warning_days"] = 7 },                             // warning without expiry
        new() { ["decimals"] = 0, ["expiration_days"] = 30, ["warning_days"] = 0 },   // non-positive
        new() { ["decimals"] = 0, ["expiration_days"] = 0 },                          // non-positive expiry
        new() { ["decimals"] = 5 },                                                   // beyond the ledger's 4 places
        new() { ["decimals"] = 1.5 },                                                 // not a whole number
    };

    [Theory]
    [MemberData(nameof(InvalidPointsConfigs))]
    public async Task Invalid_points_config_is_rejected(Dictionary<string, object> config)
    {
        var programId = await CreateProgramAsync();
        (await PostAccountTypeAsync(programId, "POINTS", config)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Points_warning_days_round_trip_and_can_be_cleared()
    {
        var programId = await CreateProgramAsync();
        var created = await ReadAccountTypeAsync(await PostAccountTypeAsync(programId, "POINTS",
            new() { ["decimals"] = 2, ["expiration_days"] = 30, ["warning_days"] = 7 }));
        created.Config.GetProperty("warning_days").GetInt32().Should().Be(7);

        var cleared = await _client.PatchAsync($"{AccountTypesUrl(programId)}/{created.Id}",
            Json(new { config = new Dictionary<string, object> { ["decimals"] = 2, ["expiration_days"] = 30 } }));
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAccountTypeAsync(cleared)).Config.TryGetProperty("warning_days", out _).Should().BeFalse();
    }

    // ── Item 1: tier-qualifying flag ─────────────────────────────────────────────────────
    [Fact]
    public async Task Only_a_points_account_type_can_be_tier_qualifying()
    {
        var programId = await CreateProgramAsync();
        var response = await PostAccountTypeAsync(programId, "CASH", new() { ["currency"] = "SAR", ["decimals"] = 2 }, isTierQualifying: true);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_program_has_at_most_one_tier_qualifying_account_type_and_exposes_it()
    {
        var programId = await CreateProgramAsync();
        var first = await ReadAccountTypeAsync(await PostAccountTypeAsync(programId, "POINTS", new() { ["decimals"] = 0 }, isTierQualifying: true));
        first.IsTierQualifying.Should().BeTrue();

        var second = await PostAccountTypeAsync(programId, "POINTS", new() { ["decimals"] = 0 }, isTierQualifying: true);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var program = await (await _client.GetAsync($"{ProgramsUrl}/{programId}")).Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options);
        program!.QualifyingAccountTypeId.Should().Be(first.Id, "the deprecated response field is derived from the flag");
        program.WarningDays.Should().BeNull();
    }

    [Fact]
    public async Task Tier_qualifying_flag_is_locked_while_the_program_is_active()
    {
        var programId = await CreateProgramAsync();
        var flagged = await ReadAccountTypeAsync(await PostAccountTypeAsync(programId, "POINTS", new() { ["decimals"] = 0 }, isTierQualifying: true));
        var other = await ReadAccountTypeAsync(await PostAccountTypeAsync(programId, "POINTS", new() { ["decimals"] = 0 }));

        (await _client.PostAsync($"{ProgramsUrl}/{programId}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PatchAsync($"{ProgramsUrl}/{programId}", Json(new UpdateProgramRequest(null, null, ProgramStatus.Active, null, null))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.PatchAsync($"{AccountTypesUrl(programId)}/{flagged.Id}", Json(new { isTierQualifying = false })))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.PatchAsync($"{AccountTypesUrl(programId)}/{other.Id}", Json(new { isTierQualifying = true })))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        // Renaming is unaffected by the lock.
        (await _client.PatchAsync($"{AccountTypesUrl(programId)}/{other.Id}", Json(new { name = "Renamed" })))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Item 1: fields that moved off Program ────────────────────────────────────────────
    [Fact]
    public async Task Program_requests_that_still_send_moved_fields_are_rejected()
    {
        var programId = await CreateProgramAsync();

        (await _client.PostAsync(ProgramsUrl, Json(new CreateProgramRequest("moved", null, WarningDays: 7))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PatchAsync($"{ProgramsUrl}/{programId}", Json(new UpdateProgramRequest(null, null, null, null, 7))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PatchAsync($"{ProgramsUrl}/{programId}", Json(new UpdateProgramRequest(null, null, null, Guid.NewGuid(), null))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Tenancy ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Another_tenants_admin_cannot_change_the_tier_flag()
    {
        var programId = await CreateProgramAsync();
        var accountType = await ReadAccountTypeAsync(await PostAccountTypeAsync(programId, "POINTS", new() { ["decimals"] = 0 }));

        Tenant otherTenant;
        await using (var db = _factory.CreateDbContext())
            otherTenant = db.Tenants.Single(t => t.Slug == OtherTenantSlug);
        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        using var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssueTenantAdminToken(jwt, otherTenant));

        var attempt = await otherClient.PatchAsync($"{AccountTypesUrl(programId)}/{accountType.Id}", Json(new { isTierQualifying = true }));
        attempt.IsSuccessStatusCode.Should().BeFalse();

        await using var verify = _factory.CreateDbContext();
        verify.AccountTypes.Single(a => a.Id == accountType.Id).IsTierQualifying.Should().BeFalse();
    }
}
