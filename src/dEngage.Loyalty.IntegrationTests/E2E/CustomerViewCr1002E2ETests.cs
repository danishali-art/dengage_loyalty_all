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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.IntegrationTests.E2E;

// CR 2026-10-02 (Customer 360) against real Postgres: migration CustomerViewCr1002 on the
// partitioned event_inbox (a partition that existed before the migration gets the column and
// index, and so does one created after), and the customer view's queries on jsonb payloads and
// partitioned tables — including a fixed-bonus streak posting, which stores the campaign id in
// ledger_entries.rule_id (no FK there in Postgres; SQLite would reject it).
// Requires Docker. Run explicitly with: dotnet test --filter Category=E2E
[Trait("Category", "E2E")]
public sealed class CustomerViewCr1002E2ETests : IAsyncLifetime
{
    private const string TenantSlug = "t1";
    private const string LateTenantSlug = "t2";
    private const string MigrationBeforeCr = "RewardCatalogFulfilmentCr0930";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private PostgresWebApplicationFactory _factory = default!;
    private HttpClient _client = default!;

    private Guid _tenantGuid;
    private Guid _programId;
    private Guid _pointsId;
    private Guid _campaignId;
    private Guid _streakEntryId;
    private readonly string _ck = "cust_e2e";
    private readonly string _orderEvent = Guid.NewGuid().ToString();
    private readonly string _streakEvent = Guid.NewGuid().ToString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _factory = new PostgresWebApplicationFactory(_container.GetConnectionString());

        // Migrate up to the CR before this one, create a tenant partition, then apply the rest —
        // the case of a live database whose tenants already have partitions.
        await using (var db = _factory.CreateDbContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeCr);
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS event_inbox_t1 PARTITION OF event_inbox FOR VALUES IN ('t1')");
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS ledger_entries_t1 PARTITION OF ledger_entries FOR VALUES IN ('t1')");
        }
        await _factory.MigrateDbAsync();

        await using (var db = _factory.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS event_inbox_t2 PARTITION OF event_inbox FOR VALUES IN ('t2')");

            var tenant = new Tenant { Id = Guid.NewGuid(), Slug = TenantSlug, Name = TenantSlug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow };
            _tenantGuid = tenant.Id;
            db.Tenants.Add(tenant);

            _programId = Guid.NewGuid();
            db.Programs.Add(new ProgramEntity { Id = _programId, TenantId = tenant.Id, Name = "Shop", Status = ProgramStatus.Active, CreatedAt = DateTime.UtcNow });
            _pointsId = Guid.NewGuid();
            db.AccountTypes.Add(new AccountTypeEntity { Id = _pointsId, TenantId = tenant.Id, ProgramId = _programId, Type = "POINTS", Name = "Shop points", Config = "{}", CreatedAt = DateTime.UtcNow });
            var account = new CustomerAccount { Id = Guid.NewGuid(), TenantId = tenant.Id, ContactKey = _ck, AccountTypeId = _pointsId, Balance = 75, UpdatedAt = DateTime.UtcNow };
            db.CustomerAccounts.Add(account);

            _campaignId = Guid.NewGuid();
            db.StreakCampaigns.Add(new StreakCampaign
            {
                Id = _campaignId, TenantId = tenant.Id, ProgramId = _programId, Name = "Weekly streak",
                Trigger = EventTypes.OrderCreated, TargetAccountTypeId = _pointsId, Config = "{}",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });

            var t0 = DateTime.UtcNow.AddMinutes(-10);
            db.EventInbox.AddRange(
                Inbox(_orderEvent, _ck, t0, new { contact_key = _ck, amount = "50", msisdn = "966500000000" }),
                Inbox(_streakEvent, _ck, t0.AddMinutes(1), new { contact_key = _ck, amount = "20" }));

            _streakEntryId = Guid.NewGuid();
            db.LedgerEntries.AddRange(
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), TenantId = TenantSlug, CustomerAccountId = account.Id, ContactKey = _ck, Delta = 50,
                    Reason = LedgerReason.Earn, SourceEventId = _orderEvent, IdempotencyKey = $"earn:{_orderEvent}", CreatedAt = t0
                },
                new LedgerEntry
                {
                    Id = _streakEntryId, TenantId = TenantSlug, CustomerAccountId = account.Id, ContactKey = _ck, Delta = 25,
                    Reason = LedgerReason.Earn, SourceEventId = _streakEvent, RuleId = _campaignId,
                    IdempotencyKey = $"streak:{_campaignId}:{_ck}:1", CreatedAt = t0.AddMinutes(1)
                });
            db.StreakLogs.Add(new StreakLog
            {
                Id = Guid.NewGuid(), TenantId = tenant.Id, CampaignId = _campaignId, ContactKey = _ck, CompletionNo = 1,
                CompletedPeriod = DateOnly.FromDateTime(t0), Periods = 4, RewardKind = "fixed_bonus",
                RewardRef = _streakEntryId.ToString(), SourceEventId = _streakEvent, CreatedAt = t0.AddMinutes(1)
            });
            await db.SaveChangesAsync();
        }

        _client = _factory.CreateClient();
        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Partitions_created_before_and_after_the_migration_get_the_contact_key_column_and_index()
    {
        await using var db = _factory.CreateDbContext();

        foreach (var partition in new[] { "event_inbox_t1", "event_inbox_t2" })
        {
            (await Scalar(db, $"SELECT count(*) FROM information_schema.columns WHERE table_name = '{partition}' AND column_name = 'contact_key'"))
                .Should().Be(1, partition);
            (await Scalar(db, $"SELECT count(*) FROM pg_indexes WHERE tablename = '{partition}' AND indexdef LIKE '%(tenant_id, contact_key, received_at)%'"))
                .Should().Be(1, partition);
        }
    }

    [Fact]
    public async Task Events_and_drawer_work_against_partitioned_jsonb_tables()
    {
        var events = await GetAsync<CursorPage<CustomerEventResponse>>($"{CustomerUrl}/events?limit=1");
        events.Data.Should().ContainSingle().Which.EventId.Should().Be(_streakEvent);
        var next = await GetAsync<CursorPage<CustomerEventResponse>>(
            $"{CustomerUrl}/events?limit=1&cursor={Uri.EscapeDataString(events.NextCursor!)}");
        next.Data.Should().ContainSingle().Which.Outcome.Should().ContainSingle().Which.NetDelta.Should().Be(50m);

        var detail = await GetAsync<CustomerEventDetailResponse>($"{CustomerUrl}/events/{_orderEvent}");
        detail.Event!.Data!.Value.GetProperty("msisdn").GetString().Should().Be("***");
        detail.Event.Data.Value.GetProperty("amount").GetString().Should().Be("50");
    }

    // CR 2026-10-06 D12: a repeated signup pays nothing; the drawer says why and links the event
    // that already paid the bonus, so support needn't query the database.
    [Fact]
    public async Task A_repeated_signup_shows_which_rule_already_paid_and_when()
    {
        var ruleId = Guid.NewGuid();
        var firstSignup = Guid.NewGuid().ToString();
        var repeatSignup = Guid.NewGuid().ToString();
        var t0 = DateTime.UtcNow.AddMinutes(-5);
        await using (var db = _factory.CreateDbContext())
        {
            db.Rules.Add(new Rule
            {
                Id = ruleId, TenantId = _tenantGuid, ProgramId = _programId, Name = "Welcome bonus",
                Type = RuleTypes.FixedBonusRule, Trigger = EventTypes.Signup, TargetAccountTypeId = _pointsId,
                Calculation = """{"amount":100}""", Priority = 10, Status = RuleStatus.Active, CurrentVersion = 1,
                CreatedAt = t0, UpdatedAt = t0
            });
            var accountId = db.CustomerAccounts.Single(a => a.ContactKey == _ck && a.AccountTypeId == _pointsId).Id;
            db.EventInbox.AddRange(
                Inbox(firstSignup, _ck, t0, new { contact_key = _ck }),
                Inbox(repeatSignup, _ck, t0.AddMinutes(2), new { contact_key = _ck }));
            db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(), TenantId = TenantSlug, CustomerAccountId = accountId, ContactKey = _ck, Delta = 100,
                Reason = LedgerReason.Earn, SourceEventId = firstSignup, RuleId = ruleId,
                IdempotencyKey = $"once:{ruleId}:{_ck}", CreatedAt = t0
            });
            await db.SaveChangesAsync();
            await db.EventInbox.Where(e => e.EventId == firstSignup || e.EventId == repeatSignup)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.EventType, EventTypes.Signup));
        }

        var repeat = await GetAsync<CustomerEventDetailResponse>($"{CustomerUrl}/events/{repeatSignup}");
        var first = await GetAsync<CustomerEventDetailResponse>($"{CustomerUrl}/events/{firstSignup}");

        var skip = repeat.OnceOnlySkips.Should().ContainSingle().Subject;
        skip.RuleId.Should().Be(ruleId);
        skip.RuleName.Should().Be("Welcome bonus");
        skip.EarlierEventId.Should().Be(firstSignup);
        first.OnceOnlySkips.Should().BeEmpty("the first signup is the one that paid");
    }

    // CR 2026-10-06 Phase 5: "expiring soon" follows each lot's own expiry date (an override), the
    // same figure PointsExpiringDetectorJob warns about.
    [Fact]
    public async Task Expiring_soon_follows_a_lot_dated_by_an_expiry_override()
    {
        var walletId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.AccountTypes.Add(new AccountTypeEntity
            {
                Id = walletId, TenantId = _tenantGuid, ProgramId = _programId, Type = "POINTS", Name = "Dated points",
                Config = """{"expiration_days": 365, "warning_days": 30}""", CreatedAt = DateTime.UtcNow
            });
            var account = new CustomerAccount { Id = Guid.NewGuid(), TenantId = _tenantGuid, ContactKey = _ck, AccountTypeId = walletId, Balance = 160, UpdatedAt = DateTime.UtcNow };
            db.CustomerAccounts.Add(account);
            db.LedgerEntries.AddRange(
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), TenantId = TenantSlug, CustomerAccountId = account.Id, ContactKey = _ck, Delta = 100,
                    Reason = LedgerReason.Earn, SourceEventId = "seed-a", IdempotencyKey = "seed-a", CreatedAt = DateTime.UtcNow.AddDays(-40)
                },
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), TenantId = TenantSlug, CustomerAccountId = account.Id, ContactKey = _ck, Delta = 60,
                    Reason = LedgerReason.Earn, SourceEventId = "seed-b", IdempotencyKey = "seed-b", CreatedAt = DateTime.UtcNow.AddDays(-5),
                    ExpiresAt = DateTime.UtcNow.AddDays(10)
                });
            await db.SaveChangesAsync();
        }

        var profile = await GetAsync<CustomerProfileResponse>(CustomerUrl);

        var wallet = profile.Programs!.SelectMany(p => p.Wallets).Single(w => w.AccountTypeId == walletId);
        wallet.ExpiringAmount.Should().Be(60m, "only the override lot expires within the 30-day warning window");
        wallet.ExpiresOn.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)));
    }

    [Fact]
    public async Task A_fixed_bonus_streak_posting_is_shown_as_its_campaign()
    {
        var ledger = await GetAsync<CursorPage<LedgerEntryResponse>>($"{CustomerUrl}/ledger?reasonGroup=earn");

        var streak = ledger.Data.Single(e => e.Id == _streakEntryId);
        streak.RuleId.Should().BeNull();
        streak.CampaignId.Should().Be(_campaignId);
        streak.CampaignName.Should().Be("Weekly streak");

        var detail = await GetAsync<CustomerEventDetailResponse>($"{CustomerUrl}/events/{_streakEvent}");
        detail.StreakCompletions.Should().ContainSingle().Which.CampaignName.Should().Be("Weekly streak");
    }

    // P2/P3: the overview, rules & caps, streaks, rewards, tier-history, card-bucket and messages
    // queries translate on Postgres.
    [Fact]
    public async Task P2_views_work_against_Postgres()
    {
        var profile = await GetAsync<CustomerProfileResponse>(CustomerUrl);
        profile.Summary!.FirstSeenAt.Should().NotBeNull();
        profile.Programs.Should().ContainSingle().Which.Streaks.Should().BeEmpty("the customer has no streak progress row");

        (await GetAsync<CursorPage<CustomerRuleFireResponse>>($"{CustomerUrl}/rule-fires")).Data.Should().BeEmpty();
        (await GetAsync<List<RuleCapUsageResponse>>($"{CustomerUrl}/cap-usage")).Should().BeEmpty();
        (await GetAsync<List<CustomerStreakResponse>>($"{CustomerUrl}/streaks")).Should().BeEmpty();
        (await GetAsync<List<CustomerRewardResponse>>($"{CustomerUrl}/rewards")).Should().BeEmpty(
            "a fixed-bonus streak grant is not a reward definition");
        (await GetAsync<List<TierHistoryEntryResponse>>($"{CustomerUrl}/tier-history")).Should().BeEmpty();

        // P3.
        (await GetAsync<List<CustomerCardBucketResponse>>($"{CustomerUrl}/card-buckets")).Should().BeEmpty();
        (await GetAsync<CursorPage<SentMessageResponse>>($"{CustomerUrl}/messages?status=published")).Data.Should().BeEmpty();
        (await GetAsync<CursorPage<LedgerEntryResponse>>($"{CustomerUrl}/ledger?ruleId={_campaignId}"))
            .Data.Should().ContainSingle().Which.Id.Should().Be(_streakEntryId);
    }

    private string CustomerUrl => $"/api/v1/tenants/{TenantSlug}/customers/{_ck}";

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(JsonConventions.Options))!;
    }

    private static async Task<long> Scalar(DbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static EventInbox Inbox(string eventId, string contactKey, DateTime receivedAt, object data) => new()
    {
        EventId = eventId, TenantId = TenantSlug, EventType = EventTypes.OrderCreated, ContactKey = contactKey,
        Payload = JsonSerializer.Serialize(new { EventId = eventId, EventType = EventTypes.OrderCreated, Tenant = TenantSlug, OccurredAt = receivedAt, Version = "1", Data = data }),
        ReceivedAt = receivedAt, ProcessedAt = receivedAt.AddSeconds(1), Status = InboxStatus.Processed
    };
}
