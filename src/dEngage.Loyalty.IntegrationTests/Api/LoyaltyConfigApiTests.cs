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
using dEngage.Loyalty.Contract;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace dEngage.Loyalty.IntegrationTests.Api;

// Platform alignment D4: tenant settings (ingestion rate limit, extra masked fields) come only from
// the tenant's own rows in loyalty_tenant_config — there is no global value to fall back to. Each
// tenant starts with LoyaltyConfigDefaults.NewTenant (the test host adds them for tenants inserted
// directly, see TenantSettingsSeedingInterceptor).
public sealed class LoyaltyConfigApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string TenantA = "d4-tenant-a";
    private const string TenantB = "d4-tenant-b";

    private readonly CustomWebApplicationFactory _factory;
    private readonly string _ck = $"d4_{Guid.NewGuid():N}"[..12];
    private readonly string _eventIdA = Guid.NewGuid().ToString();
    private readonly string _eventIdB = Guid.NewGuid().ToString();

    public LoyaltyConfigApiTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.EnsureDbCreatedAsync();
        await using var db = _factory.CreateDbContext();
        var tenantA = Ensure(db, TenantA);
        Ensure(db, TenantB);

        await SetTenantAsync(db, tenantA, LoyaltyConfigKeys.Api.RateLimitEventIngestionPerMinute, value: "42");
        await SetTenantAsync(db, tenantA, LoyaltyConfigKeys.Api.PiiExtraMaskedFields, valueJson: """["loyalty_card_no"]""");

        var data = new { contact_key = _ck, amount = "10", loyalty_card_no = "LC-998877", phone_number = "+966500000000", note = "keep" };
        db.EventInbox.Add(Inbox(TenantA, _eventIdA, data));
        db.EventInbox.Add(Inbox(TenantB, _eventIdB, data));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Each_tenant_gets_its_own_ingestion_limit()
    {
        await PostTransferAsync(TenantA);
        await PostTransferAsync(TenantB);

        var limiter = (FakeRateLimiter)_factory.Services.GetRequiredService<IRateLimiter>();
        limiter.Requests.Where(r => r.TenantId == TenantA).Should().NotBeEmpty().And.OnlyContain(r => r.LimitPerMinute == 42);
        limiter.Requests.Where(r => r.TenantId == TenantB).Should().NotBeEmpty()
            .And.OnlyContain(r => r.LimitPerMinute == 120, "tenant B keeps the value it started with");
    }

    [Fact]
    public async Task Extra_masked_fields_apply_to_that_tenant_only_and_built_in_masking_stays()
    {
        var a = await GetEventDataAsync(TenantA, _eventIdA);
        a.GetProperty("loyalty_card_no").GetString().Should().Be("***");
        a.GetProperty("phone_number").GetString().Should().Be("***");
        a.GetProperty("note").GetString().Should().Be("keep");

        var b = await GetEventDataAsync(TenantB, _eventIdB);
        b.GetProperty("loyalty_card_no").GetString().Should().Be("LC-998877");
        b.GetProperty("phone_number").GetString().Should().Be("***");
    }

    [Fact]
    public async Task There_are_no_global_rows_for_tenant_settings()
    {
        await using var db = _factory.CreateDbContext();

        (await db.LoyaltyConfig.AnyAsync(x =>
                x.ParameterName == LoyaltyConfigKeys.Api.RateLimitEventIngestionPerMinute
                || x.ParameterName == LoyaltyConfigKeys.Api.PiiExtraMaskedFields))
            .Should().BeFalse();
    }

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

    private static async Task SetTenantAsync(Schema.LoyaltyDbContext db, Tenant tenant, string parameter,
        string? value = null, string? valueJson = null) =>
        await db.LoyaltyTenantConfig.Where(x => x.TenantId == tenant.Id && x.ParameterName == parameter)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Value, value).SetProperty(x => x.ValueJson, valueJson));

    private static Tenant Ensure(Schema.LoyaltyDbContext db, string slug)
    {
        var tenant = db.Tenants.FirstOrDefault(t => t.Slug == slug);
        if (tenant is not null) return tenant;
        tenant = new Tenant { Id = Guid.NewGuid(), Slug = slug, Name = slug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow };
        db.Tenants.Add(tenant);
        db.SaveChanges(); // the test host's interceptor adds the tenant's starting settings
        return tenant;
    }

    private EventInbox Inbox(string tenantSlug, string eventId, object data) => new()
    {
        EventId = eventId, TenantId = tenantSlug, EventType = EventTypes.OrderCreated, ContactKey = _ck,
        Payload = JsonSerializer.Serialize(new { EventId = eventId, EventType = EventTypes.OrderCreated, Tenant = tenantSlug, OccurredAt = DateTime.UtcNow, Version = "1", Data = data }),
        ReceivedAt = DateTime.UtcNow, ProcessedAt = DateTime.UtcNow, Status = InboxStatus.Processed
    };
}
