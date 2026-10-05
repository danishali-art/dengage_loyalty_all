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
