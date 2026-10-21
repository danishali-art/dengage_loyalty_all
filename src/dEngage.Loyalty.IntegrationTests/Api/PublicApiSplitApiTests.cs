using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Platform;
using dEngage.Loyalty.Contract;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Api;

// Platform alignment D9: event ingestion moved from the admin Api to Public.Api, on the same
// paths. Public.Api authenticates by API key only; the admin Api keeps only the portal's event
// routes (simulate, types, status).
public sealed class PublicApiSplitApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string TenantSlug = "public-api-split";

    private readonly CustomWebApplicationFactory _factory;

    public PublicApiSplitApiTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.EnsureDbCreatedAsync();
        await using var db = _factory.CreateDbContext();
        if (!db.Tenants.Any(t => t.Slug == TenantSlug))
        {
            db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Slug = TenantSlug, Name = TenantSlug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Public_api_accepts_an_event_with_an_api_key_and_reports_its_status()
    {
        using var client = _factory.PublicApi.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", await IssueApiKeyAsync());

        var response = await client.PostAsync($"/api/v1/tenants/{TenantSlug}/events/order-created", OrderBody());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var accepted = (await response.Content.ReadFromJsonAsync<EventAcceptedResponse>(JsonConventions.Options))!;
        _factory.EventPublisher.Published.Should().Contain(e => e.EventId == accepted.EventId && e.EventType == EventTypes.OrderCreated);

        // Not consumed in this host-only test, so there's no inbox row yet: a 404 problem body
        // proves the route is served and authorised for the API key.
        var status = await client.GetAsync($"/api/v1/tenants/{TenantSlug}/events/{accepted.EventId}");
        status.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(status)).Should().Be("not_found");
    }

    [Fact]
    public async Task Public_api_ignores_a_portal_token()
    {
        using var client = _factory.PublicApi.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PlatformAdminToken());

        var response = await client.PostAsync($"/api/v1/tenants/{TenantSlug}/events/order-created", OrderBody());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(response)).Should().Be("unauthorized");
    }

    [Fact]
    public async Task Public_api_serves_no_admin_routes()
    {
        using var client = _factory.PublicApi.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", await IssueApiKeyAsync());

        var programs = await client.GetAsync($"/api/v1/tenants/{TenantSlug}/programs");
        programs.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(programs)).Should().Be("not_found");

        var simulate = await client.PostAsync($"/api/v1/tenants/{TenantSlug}/events/simulate", OrderBody());
        simulate.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, "only GET {eventId} matches that path here");
    }

    [Fact]
    public async Task The_admin_api_no_longer_accepts_ingestion()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", await IssueApiKeyAsync());

        var response = await client.PostAsync($"/api/v1/tenants/{TenantSlug}/events/order-created", OrderBody());

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await ProblemCodeAsync(response)).Should().Be("method_not_allowed");
    }

    [Fact]
    public async Task Public_api_health_check_answers()
    {
        using var client = _factory.PublicApi.CreateClient();
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static StringContent OrderBody() => new(
        JsonSerializer.Serialize(new { contactKey = "c-1", amount = "10" }, JsonConventions.Options),
        System.Text.Encoding.UTF8, "application/json");

    private string PlatformAdminToken() =>
        JwtIssuingHelper.IssuePlatformAdminToken(_factory.Services.GetRequiredService<IJwtTokenService>());

    private async Task<string> IssueApiKeyAsync()
    {
        using var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PlatformAdminToken());
        var response = await admin.PostAsync($"/api/v1/platform/tenants/{TenantSlug}/api-keys", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreateApiKeyResponse>(JsonConventions.Options))!.RawKey;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString();
    }
}
