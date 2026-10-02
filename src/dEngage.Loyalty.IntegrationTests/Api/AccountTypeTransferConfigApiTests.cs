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

// CR 2026-09-30 addendum A: the points-transfer daily limit on the account type. POINTS only;
// daily_limit must be a number > 0 (PointsTransferHandler reads it as a JSON number).
public sealed class AccountTypeTransferConfigApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string TenantSlug = "cr0930-transfer-cfg";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AccountTypeTransferConfigApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureDbCreatedAsync();
        await using (var db = _factory.CreateDbContext())
        {
            if (!db.Tenants.Any(t => t.Slug == TenantSlug))
                db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Slug = TenantSlug, Name = TenantSlug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static StringContent Json(object body) => new(
        System.Text.Json.JsonSerializer.Serialize(body, JsonConventions.Options), System.Text.Encoding.UTF8, "application/json");

    private string ProgramsUrl => $"/api/v1/tenants/{TenantSlug}/programs";

    private async Task<Guid> CreateProgramAsync()
    {
        var response = await _client.PostAsync(ProgramsUrl, Json(new CreateProgramRequest($"transfer-{Guid.NewGuid():N}"[..20], null)));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options))!.Id;
    }

    private async Task<HttpResponseMessage> PostAsync(string type, object config) =>
        await _client.PostAsync($"{ProgramsUrl}/{await CreateProgramAsync()}/account-types", Json(new
        {
            type,
            name = $"{type}-{Guid.NewGuid():N}"[..20],
            config
        }));

    [Fact]
    public async Task A_points_wallet_accepts_a_positive_daily_transfer_limit()
    {
        var response = await PostAsync("POINTS", new { decimals = 0, transfer = new { daily_limit = 5000 } });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<AccountTypeResponse>(JsonConventions.Options))!;
        created.Config.GetProperty("transfer").GetProperty("daily_limit").GetDecimal().Should().Be(5000m);
    }

    public static TheoryData<object> InvalidTransferConfigs => new()
    {
        new { daily_limit = 0 },
        new { daily_limit = -10 },
        new { daily_limit = "5000" },   // a string fails at transfer time — refused here instead
        new { },                         // no limit at all
        "yes"                            // not an object
    };

    [Theory]
    [MemberData(nameof(InvalidTransferConfigs))]
    public async Task An_invalid_transfer_limit_is_rejected(object transfer)
    {
        var response = await PostAsync("POINTS", new { decimals = 0, transfer });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cash_cannot_be_transferred_between_customers()
    {
        var response = await PostAsync("CASH", new { currency = "SAR", decimals = 2, transfer = new { daily_limit = 100 } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stamps_cannot_be_transferred_between_customers()
    {
        var response = await PostAsync("STAMP", new { stamp_target = 5, transfer = new { daily_limit = 3 } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_limit_can_be_changed_and_removed_on_an_existing_wallet()
    {
        var created = await PostAsync("POINTS", new { decimals = 0, transfer = new { daily_limit = 5000 } });
        var id = (await created.Content.ReadFromJsonAsync<AccountTypeResponse>(JsonConventions.Options))!.Id;
        var url = created.Headers.Location?.ToString()
                  ?? $"{created.RequestMessage!.RequestUri!.AbsolutePath}/{id}";

        var changed = await _client.PatchAsync(url, Json(new { config = new { decimals = 0, transfer = new { daily_limit = 250 } } }));
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        (await changed.Content.ReadFromJsonAsync<AccountTypeResponse>(JsonConventions.Options))!
            .Config.GetProperty("transfer").GetProperty("daily_limit").GetDecimal().Should().Be(250m);

        var removed = await _client.PatchAsync(url, Json(new { config = new { decimals = 0 } }));
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await removed.Content.ReadFromJsonAsync<AccountTypeResponse>(JsonConventions.Options))!
            .Config.TryGetProperty("transfer", out _).Should().BeFalse();
    }
}
