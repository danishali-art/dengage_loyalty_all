using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using dEngage.Loyalty.Api.Events;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Platform;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Api;

// Regression: POST /events/points-transfer published the request as-is, so the event carried
// "amount" / "account_type_id" while PointsTransferHandler reads "points_amount" /
// "source_account_type_id" — every transfer through this route threw in the consumer and was
// dead-lettered. The request contract is unchanged; only the published event data is mapped.
public sealed class PointsTransferRouteApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string TenantSlug = "transfer-route";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PointsTransferRouteApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

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

    // Ingestion routes take an API key, issued the same way the platform admin portal does it.
    private async Task<string> IssueApiKeyAsync()
    {
        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        using var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt));
        var response = await admin.PostAsync($"/api/v1/platform/tenants/{TenantSlug}/api-keys", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreateApiKeyResponse>(JsonConventions.Options))!.RawKey;
    }

    [Fact]
    public async Task The_published_event_carries_the_fields_the_transfer_handler_reads()
    {
        var accountTypeId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Add("X-Api-Key", await IssueApiKeyAsync());

        var body = JsonSerializer.Serialize(new
        {
            contactKey = "sender",
            targetContactKey = "receiver",
            amount = "150",
            accountTypeId
        }, JsonConventions.Options);
        var response = await _client.PostAsync($"/api/v1/tenants/{TenantSlug}/events/points-transfer",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var accepted = (await response.Content.ReadFromJsonAsync<EventAcceptedResponse>(JsonConventions.Options))!;
        var envelope = _factory.EventPublisher.Published.Single(e => e.EventId == accepted.EventId);
        envelope.EventType.Should().Be(EventTypes.PointsTransfer);

        // Read exactly as PointsTransferHandler does — this threw before the fix.
        var data = envelope.Data;
        data.GetProperty("contact_key").GetString().Should().Be("sender");
        data.GetProperty("target_contact_key").GetString().Should().Be("receiver");
        decimal.Parse(data.GetProperty("points_amount").GetString()!, CultureInfo.InvariantCulture).Should().Be(150m);
        Guid.Parse(data.GetProperty("source_account_type_id").GetString()!).Should().Be(accountTypeId);
    }
}
