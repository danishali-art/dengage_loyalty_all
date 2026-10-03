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

// CR 2026-10-02 (Customer 360) P1 through the real Nancy pipeline: programs on balances, the
// enriched and filtered ledger, the customer's events (event_inbox.contact_key), the event drawer
// (linkage → 404, masked payload, counterparty postings, rule fires, tier change, messages without
// payload, scheduled-job postings) and tenant isolation.
public sealed class CustomerViewCr1002ApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string TenantSlug = "cr1002-c360";
    private const string OtherTenantSlug = "cr1002-c360-b";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _otherTenantAdmin;

    // Unique per test: the class fixture shares one database across tests.
    private readonly string _ck = $"c360_{Guid.NewGuid():N}"[..13];
    private readonly string _counterparty = $"c360cp_{Guid.NewGuid():N}"[..15];
    private readonly string _orderEvent = Guid.NewGuid().ToString();
    private readonly string _transferEvent = Guid.NewGuid().ToString();
    private readonly string _preDeployEvent = Guid.NewGuid().ToString();
    private readonly string _otherCustomerEvent = Guid.NewGuid().ToString();
    private readonly string _otherTenantEvent = Guid.NewGuid().ToString();
    private string _expirySourceId = default!;

    private Guid _programId;
    private Guid _pointsId;
    private Guid _ruleId;
    private Guid _orderEntryId;
    private Guid _relatedMessageId;
    private Guid _tierMessageId;
    private Guid _unrelatedMessageId;
    private readonly DateTime _t0 = DateTime.UtcNow.AddHours(-2);

    public CustomerViewCr1002ApiTests(CustomWebApplicationFactory factory)
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

            _programId = Guid.NewGuid();
            var otherTenantProgramId = Guid.NewGuid();
            db.Programs.AddRange(
                new ProgramEntity { Id = _programId, TenantId = tenant.Id, Name = "Shop", Status = ProgramStatus.Active, CreatedAt = DateTime.UtcNow },
                new ProgramEntity { Id = otherTenantProgramId, TenantId = otherTenant.Id, Name = "B", Status = ProgramStatus.Active, CreatedAt = DateTime.UtcNow });

            _pointsId = Guid.NewGuid();
            var cashId = Guid.NewGuid();
            var otherTenantPointsId = Guid.NewGuid();
            db.AccountTypes.AddRange(
                Account(_pointsId, tenant.Id, _programId, "POINTS", "Shop points"),
                Account(cashId, tenant.Id, _programId, "CASH", "Shop wallet"),
                Account(otherTenantPointsId, otherTenant.Id, otherTenantProgramId, "POINTS", "B points"));

            var points = CustomerAccount(tenant.Id, _ck, _pointsId, 40);
            var cash = CustomerAccount(tenant.Id, _ck, cashId, 0);
            var counterpartyPoints = CustomerAccount(tenant.Id, _counterparty, _pointsId, 50);
            var otherTenantPoints = CustomerAccount(otherTenant.Id, _ck, otherTenantPointsId, 5);
            db.CustomerAccounts.AddRange(points, cash, counterpartyPoints, otherTenantPoints);

            _ruleId = Guid.NewGuid();
            db.Rules.Add(new Rule
            {
                Id = _ruleId, TenantId = tenant.Id, ProgramId = _programId, Name = "Order earn", Type = RuleTypes.SpendRule,
                Trigger = EventTypes.OrderCreated, Calculation = "{}", CurrentVersion = 3, TargetAccountTypeId = _pointsId,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });

            var silverId = Guid.NewGuid();
            db.TierDefinitions.Add(new TierDefinition { Id = silverId, TenantId = tenant.Id, ProgramId = _programId, Name = "silver", DisplayName = "Silver", MinPoints = 100, SortOrder = 1, CreatedAt = DateTime.UtcNow });

            // order.created after the CR: inbox row carries contact_key; earns 100 points via a rule.
            db.EventInbox.Add(Inbox(TenantSlug, _orderEvent, EventTypes.OrderCreated, _ck, _t0, new
            {
                contact_key = _ck,
                amount = "250.00",
                phone_number = "+966500000000",
                card = new { card_number = "4111111111111111", brand = "visa" },
                items = new[] { new { sku = "A-1", mobileNo = "0500000000" } },
                national_id = "1234567890",
                company = "Acme"
            }));
            _orderEntryId = Guid.NewGuid();
            db.LedgerEntries.Add(Entry(_orderEntryId, points, LedgerReason.Earn, 100, _orderEvent, _t0, _ruleId));
            db.RuleFireAudits.Add(new RuleFireAudit
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, RuleId = _ruleId, RuleVersion = 2, SourceEventId = _orderEvent,
                ContactKey = _ck, CalculationSnapshot = """{"rate":"0.4"}""", ResultingDelta = 100, LedgerEntryId = _orderEntryId,
                CreatedAt = _t0
            });
            db.TierUpgradeLogs.Add(new TierUpgradeLog
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, ContactKey = _ck, ToTierId = silverId, QualifyingPts = 100,
                SourceEventId = _orderEvent, CreatedAt = _t0
            });
            _relatedMessageId = Guid.NewGuid();
            _tierMessageId = Guid.NewGuid();
            _unrelatedMessageId = Guid.NewGuid();
            db.OutboxEvents.AddRange(
                Outbox(tenant.Id, _relatedMessageId, OutboundEventTypes.PointsEarned, _ck, new { source_event_id = _orderEvent }, null, _t0),
                Outbox(tenant.Id, _tierMessageId, OutboundEventTypes.TierChanged, _ck, new { contact_key = _ck }, $"tier_changed:{_orderEvent}:{points.Id}", _t0),
                Outbox(tenant.Id, _unrelatedMessageId, OutboundEventTypes.PointsEarned, _ck, new { source_event_id = "something-else" }, null, _t0));

            // points.transfer: debits this customer, credits the counterparty under the same event id.
            db.EventInbox.Add(Inbox(TenantSlug, _transferEvent, EventTypes.PointsTransfer, _ck, _t0.AddMinutes(10),
                new { contact_key = _ck, target_contact_key = _counterparty, points_amount = "50" }));
            db.LedgerEntries.AddRange(
                Entry(Guid.NewGuid(), points, LedgerReason.TransferOut, -50, _transferEvent, _t0.AddMinutes(10)),
                Entry(Guid.NewGuid(), counterpartyPoints, LedgerReason.TransferIn, 50, _transferEvent, _t0.AddMinutes(10)));

            // Received before the CR: no contact_key on the inbox row (no backfill) — reachable only
            // through the customer's posting.
            db.EventInbox.Add(Inbox(TenantSlug, _preDeployEvent, EventTypes.OrderCreated, null, _t0.AddDays(-40),
                new { contact_key = _ck, amount = "10" }));
            db.LedgerEntries.Add(Entry(Guid.NewGuid(), points, LedgerReason.Earn, 10, _preDeployEvent, _t0.AddDays(-40)));

            // Another customer's event, no postings for this customer.
            db.EventInbox.Add(Inbox(TenantSlug, _otherCustomerEvent, EventTypes.OrderCreated, _counterparty, _t0.AddMinutes(20),
                new { contact_key = _counterparty, amount = "5" }));

            // A scheduled job's posting: no inbound event.
            _expirySourceId = $"expire:{points.Id}:2026-10-01";
            db.LedgerEntries.Add(Entry(Guid.NewGuid(), points, LedgerReason.PointsExpired, -20, _expirySourceId, _t0.AddMinutes(30)));

            // Same contact key in another tenant.
            db.EventInbox.Add(Inbox(OtherTenantSlug, _otherTenantEvent, EventTypes.OrderCreated, _ck, _t0,
                new { contact_key = _ck, amount = "1" }));

            await db.SaveChangesAsync();
        }

        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt, "c360-admin@test.local"));
        _otherTenantAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssueTenantAdminToken(jwt, otherTenant));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Profile and Activity ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Balances_carry_their_program()
    {
        var profile = await GetAsync<CustomerProfileResponse>(CustomerUrl);

        profile.Balances.Should().HaveCount(2).And.OnlyContain(b => b.ProgramId == _programId && b.ProgramName == "Shop");
    }

    [Fact]
    public async Task Ledger_rows_show_their_event_rule_version_wallet_and_program()
    {
        var page = await GetAsync<CursorPage<LedgerEntryResponse>>($"{CustomerUrl}/ledger");

        var earn = page.Data.Single(e => e.Id == _orderEntryId);
        earn.SourceEventId.Should().Be(_orderEvent);
        earn.EventType.Should().Be(EventTypes.OrderCreated);
        earn.RuleId.Should().Be(_ruleId);
        earn.RuleName.Should().Be("Order earn");
        earn.RuleVersion.Should().Be(2, "the version the posting was made under, not the rule's current one");
        earn.AccountTypeName.Should().Be("Shop points");
        earn.AccountTypeType.Should().Be("POINTS");
        earn.ProgramId.Should().Be(_programId);
        earn.ProgramName.Should().Be("Shop");

        var expiry = page.Data.Single(e => e.Reason == LedgerReason.PointsExpired);
        expiry.EventType.Should().BeNull("a scheduled job has no inbound event");
        expiry.RuleId.Should().BeNull();
    }

    [Fact]
    public async Task Ledger_filters_by_reason_group_event_program_and_time()
    {
        var transfers = await GetAsync<CursorPage<LedgerEntryResponse>>($"{CustomerUrl}/ledger?reasonGroup=transfer");
        transfers.Data.Should().ContainSingle().Which.Reason.Should().Be(LedgerReason.TransferOut);
        transfers.Total.Should().Be(1, "the total counts the filtered rows only");

        (await GetAsync<CursorPage<LedgerEntryResponse>>($"{CustomerUrl}/ledger?eventId={_orderEvent}"))
            .Data.Should().ContainSingle().Which.Id.Should().Be(_orderEntryId);

        (await GetAsync<CursorPage<LedgerEntryResponse>>($"{CustomerUrl}/ledger?programId={Guid.NewGuid()}"))
            .Data.Should().BeEmpty();

        var from = Uri.EscapeDataString(_t0.AddMinutes(-1).ToString("O"));
        var to = Uri.EscapeDataString(_t0.AddMinutes(15).ToString("O"));
        (await GetAsync<CursorPage<LedgerEntryResponse>>($"{CustomerUrl}/ledger?from={from}&to={to}"))
            .Data.Select(e => e.Reason).Should().BeEquivalentTo([LedgerReason.Earn, LedgerReason.TransferOut]);
    }

    [Theory]
    [InlineData("ledger?reasonGroup=bogus")]
    [InlineData("ledger?programId=not-a-guid")]
    [InlineData("ledger?from=yesterday")]
    [InlineData("ledger?from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z")]
    [InlineData("events?status=bogus")]
    [InlineData("events?to=not-a-date")]
    [InlineData("events?from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z")]
    public async Task Invalid_filters_are_rejected(string pathAndQuery)
    {
        var response = await _admin.GetAsync($"{CustomerUrl}/{pathAndQuery}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    // ── Events ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Events_lists_only_this_customers_events_received_after_the_CR_with_their_outcome()
    {
        var page = await GetAsync<CursorPage<CustomerEventResponse>>($"{CustomerUrl}/events");

        page.Data.Select(e => e.EventId).Should().Equal(_transferEvent, _orderEvent);

        var order = page.Data.Single(e => e.EventId == _orderEvent);
        order.Status.Should().Be(InboxStatus.Processed);
        order.OccurredAt.Should().BeCloseTo(_t0, TimeSpan.FromSeconds(1));
        order.Outcome.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new EventWalletOutcomeResponse(_pointsId, "Shop points", "POINTS", 1, 100m));
    }

    [Fact]
    public async Task Events_filter_by_type_and_page_with_a_cursor()
    {
        (await GetAsync<CursorPage<CustomerEventResponse>>($"{CustomerUrl}/events?eventType={EventTypes.PointsTransfer}"))
            .Data.Should().ContainSingle().Which.EventId.Should().Be(_transferEvent);

        var first = await GetAsync<CursorPage<CustomerEventResponse>>($"{CustomerUrl}/events?limit=1");
        first.Data.Should().ContainSingle().Which.EventId.Should().Be(_transferEvent);
        first.NextCursor.Should().NotBeNull();
        first.Total.Should().Be(2, "the total counts every page");

        var second = await GetAsync<CursorPage<CustomerEventResponse>>(
            $"{CustomerUrl}/events?limit=1&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        second.Data.Should().ContainSingle().Which.EventId.Should().Be(_orderEvent);
        second.NextCursor.Should().BeNull();
        second.Total.Should().Be(2, "the total is the same on every page");
    }

    // ── Event drawer ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Drawer_masks_phone_and_card_or_national_id_fields_at_any_depth()
    {
        var detail = await GetAsync<CustomerEventDetailResponse>($"{CustomerUrl}/events/{_orderEvent}");

        var data = detail.Event!.Data!.Value;
        data.GetProperty("phone_number").GetString().Should().Be("***");
        data.GetProperty("national_id").GetString().Should().Be("***");
        data.GetProperty("card").GetProperty("card_number").GetString().Should().Be("***");
        data.GetProperty("items")[0].GetProperty("mobileNo").GetString().Should().Be("***");

        data.GetProperty("card").GetProperty("brand").GetString().Should().Be("visa");
        data.GetProperty("amount").GetString().Should().Be("250.00");
        data.GetProperty("company").GetString().Should().Be("Acme", "\"pan\" is an exact match, not a substring");
        data.GetProperty("contact_key").GetString().Should().Be(_ck);
    }

    [Fact]
    public async Task Drawer_shows_what_the_event_did_and_the_messages_it_caused()
    {
        var detail = await GetAsync<CustomerEventDetailResponse>($"{CustomerUrl}/events/{_orderEvent}");

        detail.Event!.EventType.Should().Be(EventTypes.OrderCreated);
        detail.Postings.Should().ContainSingle().Which.Should().Match<EventPostingResponse>(p =>
            p.Id == _orderEntryId && p.RuleName == "Order earn" && p.RuleVersion == 2 && p.AccountTypeName == "Shop points");
        detail.RuleFires.Should().ContainSingle().Which.Should().Match<RuleFireResponse>(f =>
            f.RuleId == _ruleId && f.RuleName == "Order earn" && f.RuleVersion == 2 && f.ResultingDelta == 100m);
        detail.TierChanges.Should().ContainSingle().Which.ToTierName.Should().Be("silver");

        // tier.changed carries no source_event_id; it is linked through its dedup key.
        detail.Messages.Select(m => m.EventId).Should().BeEquivalentTo([_relatedMessageId, _tierMessageId]);
    }

    [Fact]
    public async Task Messages_carry_no_payload()
    {
        var raw = await _admin.GetStringAsync($"{CustomerUrl}/events/{_orderEvent}");
        using var doc = JsonDocument.Parse(raw);

        foreach (var message in doc.RootElement.GetProperty("messages").EnumerateArray())
            message.TryGetProperty("payload", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Drawer_shows_a_transfers_counterparty_posting()
    {
        var detail = await GetAsync<CustomerEventDetailResponse>($"{CustomerUrl}/events/{_transferEvent}");

        detail.Postings.Select(p => (p.ContactKey, p.Reason, p.Delta)).Should().BeEquivalentTo(new[]
        {
            (_ck, LedgerReason.TransferOut, -50m),
            (_counterparty, LedgerReason.TransferIn, 50m)
        });
    }

    [Fact]
    public async Task Drawer_opens_an_event_received_before_the_CR_through_the_customers_posting()
    {
        var detail = await GetAsync<CustomerEventDetailResponse>($"{CustomerUrl}/events/{_preDeployEvent}");

        detail.Event.Should().NotBeNull();
        detail.Postings.Should().ContainSingle().Which.Delta.Should().Be(10m);
    }

    [Fact]
    public async Task Drawer_for_a_scheduled_job_posting_has_no_inbound_event()
    {
        var detail = await GetAsync<CustomerEventDetailResponse>(
            $"{CustomerUrl}/events/{Uri.EscapeDataString(_expirySourceId)}");

        detail.Event.Should().BeNull();
        detail.Postings.Should().ContainSingle().Which.Reason.Should().Be(LedgerReason.PointsExpired);
        detail.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Drawer_is_not_found_for_an_event_not_linked_to_the_customer()
    {
        (await _admin.GetAsync($"{CustomerUrl}/events/{_otherCustomerEvent}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _admin.GetAsync($"{CustomerUrl}/events/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Tenancy ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Another_tenants_admin_cannot_read_this_tenants_customer()
    {
        foreach (var path in new[] { "", "/ledger", "/events", $"/events/{_orderEvent}" })
            (await _otherTenantAdmin.GetAsync($"{CustomerUrl}{path}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
    }

    [Fact]
    public async Task The_same_contact_key_in_another_tenant_stays_out_of_this_tenants_view()
    {
        (await GetAsync<CursorPage<CustomerEventResponse>>($"{CustomerUrl}/events"))
            .Data.Should().NotContain(e => e.EventId == _otherTenantEvent);
        (await _admin.GetAsync($"{CustomerUrl}/events/{_otherTenantEvent}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
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

    private static AccountTypeEntity Account(Guid id, Guid tenantId, Guid programId, string type, string name) =>
        new() { Id = id, TenantId = tenantId, ProgramId = programId, Type = type, Name = name, Config = "{}", CreatedAt = DateTime.UtcNow };

    private static CustomerAccount CustomerAccount(Guid tenantId, string contactKey, Guid accountTypeId, decimal balance) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, ContactKey = contactKey, AccountTypeId = accountTypeId, Balance = balance, UpdatedAt = DateTime.UtcNow };

    private static LedgerEntry Entry(Guid id, CustomerAccount account, string reason, decimal delta, string sourceEventId,
        DateTime createdAt, Guid? ruleId = null) => new()
    {
        Id = id, TenantId = TenantSlug, CustomerAccountId = account.Id, ContactKey = account.ContactKey, Delta = delta,
        Reason = reason, SourceEventId = sourceEventId, RuleId = ruleId, IdempotencyKey = $"{reason}:{id}", CreatedAt = createdAt
    };

    // The envelope as the Consumer stores it: PascalCase members, snake_case Data.
    private static EventInbox Inbox(string tenantSlug, string eventId, string eventType, string? contactKey,
        DateTime receivedAt, object data) => new()
    {
        EventId = eventId, TenantId = tenantSlug, EventType = eventType, ContactKey = contactKey,
        Payload = JsonSerializer.Serialize(new { EventId = eventId, EventType = eventType, Tenant = tenantSlug, OccurredAt = receivedAt, Version = "1", Data = data }),
        ReceivedAt = receivedAt, ProcessedAt = receivedAt.AddSeconds(1), Status = InboxStatus.Processed
    };

    private static OutboxEvent Outbox(Guid tenantId, Guid eventId, string eventType, string contactKey, object data,
        string? dedupKey, DateTime createdAt) => new()
    {
        EventId = eventId, TenantId = tenantId, EventType = eventType, ContactKey = contactKey, DedupKey = dedupKey,
        Payload = JsonSerializer.Serialize(new { eventId, eventType, tenant = TenantSlug, occurredAt = createdAt, version = "1", data }),
        Status = OutboxStatus.Published, Attempts = 1, NextAttemptAt = createdAt, PublishedAt = createdAt, CreatedAt = createdAt
    };
}
