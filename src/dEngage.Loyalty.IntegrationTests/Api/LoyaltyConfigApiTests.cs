using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using dEngage.Loyalty.Api.Customers;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.RateLimiting;
using dEngage.Loyalty.Api.Platform;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Api;

// Platform alignment D4: the Api reads per-tenant settings from loyalty_config. A tenant row
// changes that tenant only; tenants without a row keep the deployment's default.
public sealed class LoyaltyConfigApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string ConfiguredTenant = "d4-configured";
    private const string PlainTenant = "d4-plain";

    private readonly CustomWebApplicationFactory _factory;
    private readonly string _ck = $"d4_{Guid.NewGuid():N}"[..12];
    private readonly string _eventId = Guid.NewGuid().ToString();
    private readonly string _plainEventId = Guid.NewGuid().ToString();

    public LoyaltyConfigApiTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.EnsureDbCreatedAsync();
        await using var db = _factory.CreateDbContext();

        var configured = Ensure(db, ConfiguredTenant);
        Ensure(db, PlainTenant);
        await db.SaveChangesAsync();

        if (!db.LoyaltyConfig.Any(x => x.TenantId == configured.Id))
        {
            db.LoyaltyConfig.AddRange(
                new LoyaltyConfigEntry
                {
                    ModuleName = LoyaltyConfigKeys.Api.Module, ParameterName = LoyaltyConfigKeys.Api.RateLimitEventIngestionPerMinute,
                    TenantId = configured.Id, Value = "42", UpdatedAt = DateTime.UtcNow
                },
                new LoyaltyConfigEntry
                {
                    ModuleName = LoyaltyConfigKeys.Api.Module, ParameterName = LoyaltyConfigKeys.Api.PiiExtraMaskedFields,
                    TenantId = configured.Id, ValueJson = """["loyalty_card_no"]""", UpdatedAt = DateTime.UtcNow
                });
        }

        var data = new { contact_key = _ck, amount = "10", loyalty_card_no = "LC-998877", phone_number = "+966500000000", note = "keep" };
        db.EventInbox.Add(Inbox(ConfiguredTenant, _eventId, data));
        db.EventInbox.Add(Inbox(PlainTenant, _plainEventId, data));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_tenant_row_sets_that_tenants_ingestion_limit_and_others_keep_the_default()
    {
        await PostTransferAsync(ConfiguredTenant);
        await PostTransferAsync(PlainTenant);

        var limiter = (FakeRateLimiter)_factory.Services.GetRequiredService<IRateLimiter>();
        limiter.Requests.Should().Contain(r => r.TenantId == ConfiguredTenant && r.LimitPerMinute == 42);
        // appsettings.json RateLimit:EventIngestionPerMinutePerTenant
        limiter.Requests.Should().Contain(r => r.TenantId == PlainTenant && r.LimitPerMinute == 600);
    }

    [Fact]
    public async Task Extra_masked_fields_are_masked_for_that_tenant_only_and_built_in_masking_stays()
    {
        var configured = await GetEventDataAsync(ConfiguredTenant, _eventId);
        configured.GetProperty("loyalty_card_no").GetString().Should().Be(CustomerPayloadMaskerValue);
        configured.GetProperty("phone_number").GetString().Should().Be(CustomerPayloadMaskerValue);
        configured.GetProperty("note").GetString().Should().Be("keep");

        var plain = await GetEventDataAsync(PlainTenant, _plainEventId);
        plain.GetProperty("loyalty_card_no").GetString().Should().Be("LC-998877");
        plain.GetProperty("phone_number").GetString().Should().Be(CustomerPayloadMaskerValue);
    }

    private const string CustomerPayloadMaskerValue = "***";

    private async Task PostTransferAsync(string tenantSlug)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", await IssueApiKeyAsync(tenantSlug));
        var body = JsonSerializer.Serialize(new
        {
            contactKey = "sender", targetContactKey = "receiver", amount = "1", accountTypeId = Guid.NewGuid()
        }, JsonConventions.Options);
        var response = await client.PostAsync($"/api/v1/tenants/{tenantSlug}/events/points-transfer",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> GetEventDataAsync(string tenantSlug, string eventId)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            JwtIssuingHelper.IssuePlatformAdminToken(_factory.Services.GetRequiredService<IJwtTokenService>()));
        var response = await client.GetAsync($"/api/v1/tenants/{tenantSlug}/customers/{_ck}/events/{eventId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var detail = (await response.Content.ReadFromJsonAsync<CustomerEventDetailResponse>(JsonConventions.Options))!;
        return detail.Event!.Data!.Value;
    }

    private async Task<string> IssueApiKeyAsync(string tenantSlug)
    {
        using var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            JwtIssuingHelper.IssuePlatformAdminToken(_factory.Services.GetRequiredService<IJwtTokenService>()));
        var response = await admin.PostAsync($"/api/v1/platform/tenants/{tenantSlug}/api-keys", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreateApiKeyResponse>(JsonConventions.Options))!.RawKey;
    }

    private static Tenant Ensure(Schema.LoyaltyDbContext db, string slug)
    {
        var tenant = db.Tenants.Local.FirstOrDefault(t => t.Slug == slug) ?? db.Tenants.FirstOrDefault(t => t.Slug == slug);
        if (tenant is not null) return tenant;
        tenant = new Tenant { Id = Guid.NewGuid(), Slug = slug, Name = slug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow };
        db.Tenants.Add(tenant);
        return tenant;
    }

    private EventInbox Inbox(string tenantSlug, string eventId, object data) => new()
    {
        EventId = eventId, TenantId = tenantSlug, EventType = EventTypes.OrderCreated, ContactKey = _ck,
        Payload = JsonSerializer.Serialize(new { EventId = eventId, EventType = EventTypes.OrderCreated, Tenant = tenantSlug, OccurredAt = DateTime.UtcNow, Version = "1", Data = data }),
        ReceivedAt = DateTime.UtcNow, ProcessedAt = DateTime.UtcNow, Status = InboxStatus.Processed
    };
}
