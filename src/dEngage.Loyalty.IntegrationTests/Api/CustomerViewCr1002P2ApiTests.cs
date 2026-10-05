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

// CR 2026-10-02 (Customer 360) P2 through the real Nancy pipeline: header summary, per-program
// overview (pending, FIFO expiring-soon, tier status, streaks), rule fires, per-customer cap usage
// from the ledger, streaks, rewards with their payouts, tier-change cause — and tenant isolation.
// Day-relative data uses the current UTC date, the same clock the endpoints use.
public sealed class CustomerViewCr1002P2ApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string TenantSlug = "cr1002-c360-p2";
    private const string OtherTenantSlug = "cr1002-c360-p2-b";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _otherTenantAdmin;

    // Unique per test: the class fixture shares one database across tests.
    private readonly string _ck = $"p2_{Guid.NewGuid():N}"[..11];
    private readonly string _orderEvent = Guid.NewGuid().ToString();
    private readonly string _purchaseEvent = Guid.NewGuid().ToString();
    private readonly string _streakEvent = Guid.NewGuid().ToString();
    private readonly DateTime _today = DateTime.UtcNow.Date;

    private Guid _programId;
    private Guid _pointsId;
    private Guid _cashId;
    private Guid _ruleId;
    private Guid _campaignId;
    private Guid _streakRewardId;
    private Guid _purchaseRewardId;

    public CustomerViewCr1002P2ApiTests(CustomWebApplicationFactory factory)
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
            db.Programs.Add(new ProgramEntity { Id = _programId, TenantId = tenant.Id, Name = "Shop", Status = ProgramStatus.Active, CreatedAt = DateTime.UtcNow });

            _pointsId = Guid.NewGuid();
            _cashId = Guid.NewGuid();
            db.AccountTypes.AddRange(
                new AccountTypeEntity { Id = _pointsId, TenantId = tenant.Id, ProgramId = _programId, Type = "POINTS", Name = "Shop points", Config = """{"expiration_days": 60, "warning_days": 30}""", IsTierQualifying = true, CreatedAt = DateTime.UtcNow },
                new AccountTypeEntity { Id = _cashId, TenantId = tenant.Id, ProgramId = _programId, Type = "CASH", Name = "Wallet", Config = """{"currency": "SAR", "decimals": 2}""", CreatedAt = DateTime.UtcNow });

            var silverId = Guid.NewGuid();
            var goldId = Guid.NewGuid();
            db.TierDefinitions.AddRange(
                new TierDefinition { Id = silverId, TenantId = tenant.Id, ProgramId = _programId, Name = "silver", DisplayName = "Silver", MinPoints = 100, SortOrder = 1, CreatedAt = DateTime.UtcNow },
                new TierDefinition { Id = goldId, TenantId = tenant.Id, ProgramId = _programId, Name = "gold", DisplayName = "Gold", MinPoints = 500, SortOrder = 2, CreatedAt = DateTime.UtcNow });

            var points = new CustomerAccount
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, ContactKey = _ck, AccountTypeId = _pointsId, Balance = 150,
                TierId = silverId, TierQualifyingPts = 150, TierPeriodStart = DateOnly.FromDateTime(_today.AddDays(-50)),
                TierExpiresAt = DateOnly.FromDateTime(_today.AddDays(40)), TierLockedUntil = DateOnly.FromDateTime(_today.AddDays(10)),
                UpdatedAt = _today
            };
            var cash = new CustomerAccount { Id = Guid.NewGuid(), TenantId = tenant.Id, ContactKey = _ck, AccountTypeId = _cashId, Balance = 15, UpdatedAt = _today };
            db.CustomerAccounts.AddRange(points, cash);

            _ruleId = Guid.NewGuid();
            db.Rules.Add(new Rule
            {
                Id = _ruleId, TenantId = tenant.Id, ProgramId = _programId, Name = "Order earn", Type = RuleTypes.SpendRule,
                Trigger = EventTypes.OrderCreated, Calculation = "{}", CurrentVersion = 2, TargetAccountTypeId = _pointsId,
                Limits = """{"per_customer_total": 500, "per_customer_per_day": 50, "per_customer_per_period": 200, "period": "Month"}""",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });

            // Points: earned 100 (rule, 40 days ago) and 80 (35 days ago), 20 today (rule), a
            // 5-point refund today (rule), 30 spent on a reward. FIFO: the 35 consumed come out of
            // the oldest earn first, so 65 + 80 = 145 is left from before the warning cutoff
            // (today − 30), dated from the earn 40 days ago.
            var purchaseEntryId = Guid.NewGuid();
            db.LedgerEntries.AddRange(
                Entry(points, LedgerReason.Earn, 100, _orderEvent, _today.AddDays(-40), _ruleId),
                Entry(points, LedgerReason.Earn, 80, Guid.NewGuid().ToString(), _today.AddDays(-35)),
                Entry(points, LedgerReason.Earn, 20, Guid.NewGuid().ToString(), _today, _ruleId),
                Entry(points, LedgerReason.Refund, -5, Guid.NewGuid().ToString(), _today, _ruleId),
                Entry(points, LedgerReason.RewardPurchase, -30, _purchaseEvent, _today.AddDays(-10), id: purchaseEntryId),
                Entry(cash, LedgerReason.RewardCashback, 5, _purchaseEvent, _today.AddDays(-10), key: $"{_purchaseEvent}:reward_cashback"));
            db.HeldPostings.Add(new HeldPosting
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, RuleId = _ruleId, CustomerAccountId = points.Id, ContactKey = _ck,
                Delta = 25, Reason = LedgerReason.Earn, SourceEventId = Guid.NewGuid().ToString(), IdempotencyKey = Guid.NewGuid().ToString(),
                HoldUntil = _today.AddDays(3), CreatedAt = _today
            });

            db.RuleFireAudits.AddRange(
                Fire(tenant.Id, _orderEvent, _today.AddDays(-40)),
                Fire(tenant.Id, Guid.NewGuid().ToString(), _today));

            // Rewards: a cashback bought with points, and one granted by a streak completion.
            _purchaseRewardId = Guid.NewGuid();
            _streakRewardId = Guid.NewGuid();
            db.RewardDefinitions.AddRange(
                Reward(_purchaseRewardId, tenant.Id, $"{_ck}_coffee", RewardAcquisition.PointsPurchase),
                Reward(_streakRewardId, tenant.Id, $"{_ck}_streak_cash", RewardAcquisition.StreakCompletion));
            db.RewardLogs.Add(new RewardLog
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, ContactKey = _ck, AccountTypeId = _pointsId, RewardName = $"{_ck}_coffee",
                SourceEventId = _purchaseEvent, LedgerResetEntryId = purchaseEntryId, CompletionCount = 1,
                Status = RewardLogStatus.Notified, RewardDefinitionId = _purchaseRewardId, CreatedAt = _today.AddDays(-10)
            });

            _campaignId = Guid.NewGuid();
            db.StreakCampaigns.Add(new StreakCampaign
            {
                Id = _campaignId, TenantId = tenant.Id, ProgramId = _programId, Name = "Weekly coffee", Trigger = EventTypes.OrderCreated,
                TargetAccountTypeId = _pointsId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                Config = $$$"""{"period":"week","target_periods":4,"aggregate":{"metric":"count","threshold":1},"timezone":"UTC","on_complete":"restart","reward":{"kind":"reward_definition","reward_definition_id":"{{{_streakRewardId}}}"}}"""
            });
            db.StreakProgresses.Add(new StreakProgress
            {
                TenantId = tenant.Id, CampaignId = _campaignId, ContactKey = _ck, StreakCount = 2, Completions = 1,
                LastMetPeriod = DateOnly.FromDateTime(_today.AddDays(-7)), Status = StreakProgressStatus.Active, UpdatedAt = _today
            });
            db.StreakPeriodStates.AddRange(
                new StreakPeriodState { TenantId = tenant.Id, CampaignId = _campaignId, ContactKey = _ck, PeriodStart = DateOnly.FromDateTime(_today.AddDays(-14)), AggCount = 3, Met = true, UpdatedAt = _today },
                new StreakPeriodState { TenantId = tenant.Id, CampaignId = _campaignId, ContactKey = _ck, PeriodStart = DateOnly.FromDateTime(_today.AddDays(-7)), AggCount = 1, Met = true, UpdatedAt = _today });
            db.StreakLogs.Add(new StreakLog
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, CampaignId = _campaignId, ContactKey = _ck, CompletionNo = 1,
                CompletedPeriod = DateOnly.FromDateTime(_today.AddDays(-21)), Periods = 4, RewardKind = "reward_definition",
                SourceEventId = _streakEvent, CreatedAt = _today.AddDays(-21)
            });
            db.LedgerEntries.Add(Entry(cash, LedgerReason.RewardCashback, 10, _streakEvent, _today.AddDays(-21),
                key: $"streak:{_campaignId}:{_ck}:1:reward_cashback"));

            // Tier changes: from points, from a reward, from the nightly downgrade.
            db.TierUpgradeLogs.AddRange(
                TierLog(tenant.Id, null, silverId, _orderEvent, _today.AddDays(-40)),
                TierLog(tenant.Id, silverId, goldId, $"streak:{_campaignId}:{_ck}:1:tier_upgrade", _today.AddDays(-21)),
                TierLog(tenant.Id, goldId, silverId, $"downgrade:{points.Id}:2026-10-01", _today.AddDays(-1)));

            // Failed events: one within the last 7 days counts, an older one doesn't.
            db.EventInbox.AddRange(
                Inbox(Guid.NewGuid().ToString(), InboxStatus.Failed, _today.AddDays(-1)),
                Inbox(Guid.NewGuid().ToString(), InboxStatus.Failed, _today.AddDays(-10)));

            await db.SaveChangesAsync();
        }

        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt, "c360-p2@test.local"));
        _otherTenantAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssueTenantAdminToken(jwt, otherTenant));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Profile_carries_the_header_summary()
    {
        var profile = await GetAsync<CustomerProfileResponse>(CustomerUrl);

        profile.Summary!.FirstSeenAt.Should().Be(_today.AddDays(-40));
        profile.Summary.FailedEventsLast7Days.Should().Be(1);
        profile.Summary.ActiveStreakCount.Should().Be(1);
    }

    [Fact]
    public async Task Overview_shows_pending_and_FIFO_expiring_soon_per_wallet()
    {
        var program = (await GetAsync<CustomerProfileResponse>(CustomerUrl)).Programs!.Should().ContainSingle().Subject;
        program.ProgramName.Should().Be("Shop");

        var points = program.Wallets.Single(w => w.AccountTypeId == _pointsId);
        points.PendingAmount.Should().Be(25m);
        points.ExpiringAmount.Should().Be(145m);
        points.ExpiresOn.Should().Be(DateOnly.FromDateTime(_today.AddDays(-40 + 60)));

        var cash = program.Wallets.Single(w => w.AccountTypeId == _cashId);
        cash.Currency.Should().Be("SAR");
        cash.ExpiringAmount.Should().BeNull();
    }

    [Fact]
    public async Task Overview_shows_the_programs_tier_status_and_streaks()
    {
        var program = (await GetAsync<CustomerProfileResponse>(CustomerUrl)).Programs!.Single();

        program.Tier!.TierName.Should().Be("silver");
        program.Tier.NextTierName.Should().Be("gold");
        program.Tier.NextTierMinPoints.Should().Be(500m);
        program.Tier.QualifyingPoints.Should().Be(150m);
        program.Tier.GraceEndsAt.Should().Be(DateOnly.FromDateTime(_today.AddDays(40)));
        program.Tier.LockedUntil.Should().Be(DateOnly.FromDateTime(_today.AddDays(10)));

        program.Streaks.Should().ContainSingle().Which.Should().Be(
            new StreakSummaryResponse(_campaignId, "Weekly coffee", 2, 4, 1, StreakProgressStatus.Active));
    }

    [Fact]
    public async Task Rule_fires_page_newest_first_and_filter_by_rule()
    {
        var first = await GetAsync<CursorPage<CustomerRuleFireResponse>>($"{CustomerUrl}/rule-fires?limit=1");
        first.Data.Should().ContainSingle().Which.CreatedAt.Should().Be(_today);
        first.Total.Should().Be(2);
        first.Data[0].RuleName.Should().Be("Order earn");

        var second = await GetAsync<CursorPage<CustomerRuleFireResponse>>(
            $"{CustomerUrl}/rule-fires?limit=1&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        second.Data.Should().ContainSingle().Which.SourceEventId.Should().Be(_orderEvent);
        second.NextCursor.Should().BeNull();

        (await GetAsync<CursorPage<CustomerRuleFireResponse>>($"{CustomerUrl}/rule-fires?ruleId={Guid.NewGuid()}"))
            .Data.Should().BeEmpty();
    }

    [Theory]
    [InlineData("rule-fires?ruleId=nope")]
    [InlineData("rule-fires?from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z")]
    public async Task Invalid_rule_fire_filters_are_rejected(string pathAndQuery) =>
        (await _admin.GetAsync($"{CustomerUrl}/{pathAndQuery}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Cap_usage_is_counted_from_the_ledger_like_the_engine()
    {
        var usage = (await GetAsync<List<RuleCapUsageResponse>>($"{CustomerUrl}/cap-usage")).Should().ContainSingle().Subject;

        usage.RuleId.Should().Be(_ruleId);
        usage.IsCardBucket.Should().BeFalse();
        usage.PerCustomerTotal.Should().Be(500m);
        usage.UsedTotal.Should().Be(115m, "100 + 20 earned, less the 5 refunded");
        usage.PerCustomerPerDay.Should().Be(50m);
        usage.UsedToday.Should().Be(15m);
        usage.PerCustomerPerPeriod.Should().Be(200m);
        usage.Period.Should().Be("Month");
        usage.ResetWindow.Should().Be("Calendar");
        usage.UsedThisPeriod.Should().Be(15m);
    }

    [Fact]
    public async Task Streaks_show_progress_latest_period_and_history()
    {
        var streak = (await GetAsync<List<CustomerStreakResponse>>($"{CustomerUrl}/streaks")).Should().ContainSingle().Subject;

        streak.CampaignName.Should().Be("Weekly coffee");
        streak.TargetPeriods.Should().Be(4);
        streak.StreakCount.Should().Be(2);
        streak.LatestPeriodStart.Should().Be(DateOnly.FromDateTime(_today.AddDays(-7)));
        streak.LatestAggCount.Should().Be(1);
        streak.History.Should().ContainSingle().Which.CompletionNo.Should().Be(1);
    }

    [Fact]
    public async Task Rewards_list_purchases_and_streak_grants_with_their_payouts()
    {
        var rewards = await GetAsync<List<CustomerRewardResponse>>($"{CustomerUrl}/rewards");

        rewards.Should().HaveCount(2);
        var bought = rewards.Single(r => r.Source == RewardAcquisition.PointsPurchase);
        bought.RewardDefinitionId.Should().Be(_purchaseRewardId);
        bought.Cost.Should().Be(30m);
        bought.CostWalletName.Should().Be("Shop points");
        bought.Outcome.Should().Be("cash_credited");
        bought.CashAmount.Should().Be(5m);
        bought.CashWalletName.Should().Be("Wallet");

        var earned = rewards.Single(r => r.Source == RewardAcquisition.StreakCompletion);
        earned.RewardDefinitionId.Should().Be(_streakRewardId);
        earned.Cost.Should().BeNull();
        earned.CashAmount.Should().Be(10m);
        earned.SourceEventId.Should().Be(_streakEvent);
    }

    [Fact]
    public async Task Tier_history_shows_program_and_cause()
    {
        var history = await GetAsync<List<TierHistoryEntryResponse>>($"{CustomerUrl}/tier-history");

        history.Select(h => h.Cause).Should().Equal("downgrade", "reward", "points");
        history.Should().OnlyContain(h => h.ProgramId == _programId && h.ProgramName == "Shop");
    }

    [Fact]
    public async Task Another_tenants_admin_cannot_read_the_P2_views()
    {
        foreach (var path in new[] { "/rule-fires", "/cap-usage", "/streaks", "/rewards" })
            (await _otherTenantAdmin.GetAsync($"{CustomerUrl}{path}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
    }

    [Fact]
    public async Task The_same_contact_key_in_another_tenant_has_nothing_here()
    {
        var url = $"/api/v1/tenants/{OtherTenantSlug}/customers/{_ck}";
        (await GetAsync<List<RuleCapUsageResponse>>($"{url}/cap-usage")).Should().BeEmpty();
        (await GetAsync<List<CustomerStreakResponse>>($"{url}/streaks")).Should().BeEmpty();
        (await GetAsync<List<CustomerRewardResponse>>($"{url}/rewards")).Should().BeEmpty();
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

    private static LedgerEntry Entry(CustomerAccount account, string reason, decimal delta, string sourceEventId,
        DateTime createdAt, Guid? ruleId = null, Guid? id = null, string? key = null)
    {
        var entryId = id ?? Guid.NewGuid();
        return new LedgerEntry
        {
            Id = entryId, TenantId = TenantSlug, CustomerAccountId = account.Id, ContactKey = account.ContactKey,
            Delta = delta, Reason = reason, SourceEventId = sourceEventId, RuleId = ruleId,
            IdempotencyKey = key ?? $"{reason}:{entryId}", CreatedAt = createdAt
        };
    }

    private RuleFireAudit Fire(Guid tenantId, string sourceEventId, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, RuleId = _ruleId, RuleVersion = 1, SourceEventId = sourceEventId,
        ContactKey = _ck, CalculationSnapshot = "{}", ResultingDelta = 10, CreatedAt = createdAt
    };

    private RewardDefinition Reward(Guid id, Guid tenantId, string name, string acquisition) => new()
    {
        Id = id, TenantId = tenantId, ProgramId = _programId, Name = name, DisplayName = name, Acquisition = acquisition,
        RewardType = RewardType.Cashback, TypeConfig = $$"""{"amount":"5","currency":"SAR","cash_account_type_id":"{{_cashId}}"}""",
        CreatedAt = DateTime.UtcNow
    };

    private TierUpgradeLog TierLog(Guid tenantId, Guid? from, Guid to, string sourceEventId, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, ContactKey = _ck, FromTierId = from, ToTierId = to, QualifyingPts = 150,
        SourceEventId = sourceEventId, CreatedAt = createdAt
    };

    private EventInbox Inbox(string eventId, string status, DateTime receivedAt) => new()
    {
        EventId = eventId, TenantId = TenantSlug, EventType = EventTypes.OrderCreated, ContactKey = _ck,
        Payload = JsonSerializer.Serialize(new { EventId = eventId, Data = new { contact_key = _ck } }),
        ReceivedAt = receivedAt, Status = status, Error = status == InboxStatus.Failed ? "boom" : null
    };
}
