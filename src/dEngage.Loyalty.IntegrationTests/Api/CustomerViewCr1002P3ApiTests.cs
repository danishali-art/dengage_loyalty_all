using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using dEngage.Loyalty.Api.Customers;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using dEngage.Loyalty.Shared.Events;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.IntegrationTests.Api;

// CR 2026-10-02 (Customer 360) P3 through the real Nancy pipeline: the customer's card buckets
// with usage from the ledger, a bucket's postings through the ledger's ruleId filter, and messages
// sent (no payload, filters, paging) — plus tenant isolation.
public sealed class CustomerViewCr1002P3ApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string TenantSlug = "cr1002-c360-p3";
    private const string OtherTenantSlug = "cr1002-c360-p3-b";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _otherTenantAdmin;

    // Unique per test: the class fixture shares one database across tests.
    private readonly string _ck = $"p3_{Guid.NewGuid():N}"[..11];
    private readonly DateTime _today = DateTime.UtcNow.Date;

    private Guid _bucketId;
    private Guid _publishedId;
    private Guid _failedId;
    private Guid _pendingId;

    public CustomerViewCr1002P3ApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _admin = factory.CreateClient();
        _otherTenantAdmin = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureDbCreatedAsync();
        Tenant tenant, otherTenant;
        await using (var db = _factory.CreateDbContext())
        {
            tenant = Ensure(db, TenantSlug);
            otherTenant = Ensure(db, OtherTenantSlug);

            var programId = Guid.NewGuid();
            db.Programs.Add(new ProgramEntity { Id = programId, TenantId = tenant.Id, Name = "Card", Status = ProgramStatus.Active, CreatedAt = DateTime.UtcNow });
            var pointsId = Guid.NewGuid();
            db.AccountTypes.Add(new AccountTypeEntity { Id = pointsId, TenantId = tenant.Id, ProgramId = programId, Type = "POINTS", Name = "Card points", Config = "{}", CreatedAt = DateTime.UtcNow });
            var points = new CustomerAccount { Id = Guid.NewGuid(), TenantId = tenant.Id, ContactKey = _ck, AccountTypeId = pointsId, Balance = 90, UpdatedAt = _today };
            db.CustomerAccounts.Add(points);

            _bucketId = Guid.NewGuid();
            var plainRuleId = Guid.NewGuid();
            db.Rules.AddRange(
                Rule(_bucketId, tenant.Id, programId, pointsId, "Groceries bucket", "card_bucket", RuleStatus.Active,
                    """{"per_customer_per_day": 100, "per_customer_total": 300}"""),
                Rule(Guid.NewGuid(), tenant.Id, programId, pointsId, "Old bucket", "card_bucket", RuleStatus.Deleted, null),
                Rule(plainRuleId, tenant.Id, programId, pointsId, "Plain rule", null, RuleStatus.Active, null));

            // 50 three days ago, 50 today, 10 refunded today; a plain rule's posting is not the bucket's.
            db.LedgerEntries.AddRange(
                Entry(points, LedgerReason.Earn, 50, _today.AddDays(-3), _bucketId),
                Entry(points, LedgerReason.Earn, 50, _today, _bucketId),
                Entry(points, LedgerReason.Refund, -10, _today, _bucketId),
                Entry(points, LedgerReason.Earn, 7, _today, plainRuleId));

            _publishedId = Guid.NewGuid();
            _failedId = Guid.NewGuid();
            _pendingId = Guid.NewGuid();
            db.OutboxEvents.AddRange(
                Outbox(tenant.Id, _publishedId, OutboundEventTypes.PointsEarned, _ck, OutboxStatus.Published, _today.AddDays(-2)),
                Outbox(tenant.Id, _failedId, OutboundEventTypes.TierChanged, _ck, OutboxStatus.Failed, _today.AddDays(-1)),
                Outbox(tenant.Id, _pendingId, OutboundEventTypes.PointsTransferFailed, _ck, OutboxStatus.Pending, _today, reason: "no_rule"),
                Outbox(tenant.Id, Guid.NewGuid(), OutboundEventTypes.PointsEarned, "someone_else", OutboxStatus.Published, _today));

            await db.SaveChangesAsync();
        }

        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt, "c360-p3@test.local"));
        _otherTenantAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssueTenantAdminToken(jwt, otherTenant));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Card_buckets_show_reward_caps_and_usage_from_the_ledger()
    {
        var bucket = (await GetAsync<List<CustomerCardBucketResponse>>($"{CustomerUrl}/card-buckets"))
            .Should().ContainSingle("deleted buckets and plain rules are left out").Subject;

        bucket.RuleId.Should().Be(_bucketId);
        bucket.Name.Should().Be("Groceries bucket");
        bucket.RewardAmount.Should().Be(50m);
        bucket.PerCustomerPerDay.Should().Be(100m);
        bucket.UsedToday.Should().Be(40m, "50 earned less 10 refunded");
        bucket.PerCustomerTotal.Should().Be(300m);
        bucket.UsedTotal.Should().Be(90m);
        bucket.Postings.Should().Be(2);
        bucket.LastPostedAt.Should().Be(_today);
    }

    [Fact]
    public async Task The_ledger_filters_by_rule_for_a_buckets_postings()
    {
        var page = await GetAsync<CursorPage<LedgerEntryResponse>>($"{CustomerUrl}/ledger?ruleId={_bucketId}");

        page.Data.Should().HaveCount(3).And.OnlyContain(e => e.RuleId == _bucketId);
    }

    [Fact]
    public async Task Messages_list_the_customers_outbound_events_without_payload()
    {
        var raw = await _admin.GetStringAsync($"{CustomerUrl}/messages");
        using var doc = JsonDocument.Parse(raw);

        var data = doc.RootElement.GetProperty("data");
        data.GetArrayLength().Should().Be(3, "another customer's message is not listed");
        foreach (var message in data.EnumerateArray())
            message.TryGetProperty("payload", out _).Should().BeFalse();
    }

    // Addendum C (D5 amended): a *_failed message shows its reason — and still nothing else from
    // the payload.
    [Fact]
    public async Task Messages_show_a_failure_reason_but_no_other_payload_field()
    {
        var raw = await _admin.GetStringAsync($"{CustomerUrl}/messages");
        using var doc = JsonDocument.Parse(raw);

        var byId = doc.RootElement.GetProperty("data").EnumerateArray()
            .ToDictionary(m => m.GetProperty("eventId").GetGuid());
        byId[_pendingId].GetProperty("reason").GetString().Should().Be("no_rule");
        // Null fields are omitted on the wire (JsonConventions), so no reason means no key.
        (!byId[_publishedId].TryGetProperty("reason", out var none) || none.ValueKind == JsonValueKind.Null)
            .Should().BeTrue();
        foreach (var message in byId.Values)
        {
            message.TryGetProperty("balance", out _).Should().BeFalse();
            message.TryGetProperty("phone", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Messages_filter_by_status_and_type_and_page_newest_first()
    {
        (await GetAsync<CursorPage<SentMessageResponse>>($"{CustomerUrl}/messages?status=failed"))
            .Data.Should().ContainSingle().Which.EventId.Should().Be(_failedId);
        (await GetAsync<CursorPage<SentMessageResponse>>($"{CustomerUrl}/messages?eventType={OutboundEventTypes.TierChanged}"))
            .Data.Should().ContainSingle().Which.EventId.Should().Be(_failedId);

        var first = await GetAsync<CursorPage<SentMessageResponse>>($"{CustomerUrl}/messages?limit=2");
        first.Data.Select(m => m.EventId).Should().Equal(_pendingId, _failedId);
        first.Total.Should().Be(3);
        var second = await GetAsync<CursorPage<SentMessageResponse>>(
            $"{CustomerUrl}/messages?limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        second.Data.Should().ContainSingle().Which.EventId.Should().Be(_publishedId);
        second.NextCursor.Should().BeNull();
    }

    [Theory]
    [InlineData("messages?status=sent")]
    [InlineData("messages?from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z")]
    [InlineData("messages?to=soon")]
    [InlineData("ledger?ruleId=bucket")]
    public async Task Invalid_filters_are_rejected(string pathAndQuery) =>
        (await _admin.GetAsync($"{CustomerUrl}/{pathAndQuery}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Another_tenants_admin_cannot_read_the_P3_views()
    {
        foreach (var path in new[] { "/card-buckets", "/messages" })
            (await _otherTenantAdmin.GetAsync($"{CustomerUrl}{path}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
    }

    [Fact]
    public async Task The_same_contact_key_in_another_tenant_has_nothing_here()
    {
        var url = $"/api/v1/tenants/{OtherTenantSlug}/customers/{_ck}";
        (await GetAsync<List<CustomerCardBucketResponse>>($"{url}/card-buckets")).Should().BeEmpty();
        (await GetAsync<CursorPage<SentMessageResponse>>($"{url}/messages")).Data.Should().BeEmpty();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private string CustomerUrl => $"/api/v1/tenants/{TenantSlug}/customers/{_ck}";

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await _admin.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(JsonConventions.Options))!;
    }

    private static Tenant Ensure(dEngage.Loyalty.Schema.LoyaltyDbContext db, string slug)
    {
        var tenant = db.Tenants.SingleOrDefault(t => t.Slug == slug);
        if (tenant is not null) return tenant;
        tenant = new Tenant { Id = Guid.NewGuid(), Slug = slug, Name = slug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow };
        db.Tenants.Add(tenant);
        return tenant;
    }

    private static Rule Rule(Guid id, Guid tenantId, Guid programId, Guid targetId, string name, string? template,
        string status, string? limits) => new()
    {
        Id = id, TenantId = tenantId, ProgramId = programId, Name = name, Type = RuleTypes.FixedBonusRule,
        Trigger = EventTypes.CardTransaction, Calculation = """{"amount": 50}""", TargetAccountTypeId = targetId,
        Template = template, Status = status, Limits = limits, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static LedgerEntry Entry(CustomerAccount account, string reason, decimal delta, DateTime createdAt, Guid ruleId)
    {
        var id = Guid.NewGuid();
        return new LedgerEntry
        {
            Id = id, TenantId = TenantSlug, CustomerAccountId = account.Id, ContactKey = account.ContactKey, Delta = delta,
            Reason = reason, SourceEventId = Guid.NewGuid().ToString(), RuleId = ruleId, IdempotencyKey = $"{reason}:{id}",
            CreatedAt = createdAt
        };
    }

    private static OutboxEvent Outbox(Guid tenantId, Guid eventId, string eventType, string contactKey, string status,
        DateTime createdAt, string? reason = null) => new()
    {
        EventId = eventId, TenantId = tenantId, EventType = eventType, ContactKey = contactKey,
        Payload = reason is null
            ? JsonSerializer.Serialize(new { eventId, data = new { contact_key = contactKey, phone = "+966" } })
            : JsonSerializer.Serialize(new { eventId, data = new { contact_key = contactKey, phone = "+966", reason, balance = "250.00" } }),
        Status = status, Attempts = status == OutboxStatus.Failed ? 8 : 1, NextAttemptAt = createdAt,
        PublishedAt = status == OutboxStatus.Published ? createdAt : null, CreatedAt = createdAt
    };
}
