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
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Api;

// CR 2026-10-06 Phase 4: a program's default_rounding — the direction its rules inherit.
public sealed class ProgramDefaultRoundingCr1006ApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string TenantSlug = "cr1006-rounding";
    private const string ProgramsUrl = $"/api/v1/tenants/{TenantSlug}/programs";

    public ProgramDefaultRoundingCr1006ApiTests(CustomWebApplicationFactory factory)
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
            {
                db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Slug = TenantSlug, Name = TenantSlug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }
        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static StringContent Json(object body) => new(
        JsonSerializer.Serialize(body, JsonConventions.Options), System.Text.Encoding.UTF8, "application/json");

    private async Task<ProgramResponse> CreateAsync(string? defaultRounding = null)
    {
        var response = await _client.PostAsync(ProgramsUrl,
            Json(new CreateProgramRequest($"round-{Guid.NewGuid():N}", null, DefaultRounding: defaultRounding)));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options))!;
    }

    private async Task<HttpResponseMessage> UpdateAsync(Guid id, string defaultRounding) =>
        await _client.PatchAsync($"{ProgramsUrl}/{id}", Json(new UpdateProgramRequest(null, null, null, DefaultRounding: defaultRounding)));

    [Fact]
    public async Task A_new_program_rounds_down_unless_told_otherwise()
    {
        (await CreateAsync()).DefaultRounding.Should().Be(RoundingDirection.Down);
        (await CreateAsync(RoundingDirection.Nearest)).DefaultRounding.Should().Be(RoundingDirection.Nearest);
    }

    [Fact]
    public async Task An_unknown_direction_is_rejected()
    {
        var response = await _client.PostAsync(ProgramsUrl, Json(new CreateProgramRequest($"round-{Guid.NewGuid():N}", null, DefaultRounding: "sideways")));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var program = await CreateAsync();
        (await UpdateAsync(program.Id, "sideways")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Changing_it_on_a_published_program_marks_a_publish_pending()
    {
        var program = await CreateAsync();
        var accountType = await _client.PostAsync($"{ProgramsUrl}/{program.Id}/account-types",
            Json(new { type = "POINTS", name = "pts", config = new { decimals = 0 } }));
        accountType.StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.PostAsync($"{ProgramsUrl}/{program.Id}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await UpdateAsync(program.Id, RoundingDirection.Up);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var updated = (await response.Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options))!;
        updated.DefaultRounding.Should().Be(RoundingDirection.Up);
        updated.HasUnpublishedChanges.Should().BeTrue();
    }
}
