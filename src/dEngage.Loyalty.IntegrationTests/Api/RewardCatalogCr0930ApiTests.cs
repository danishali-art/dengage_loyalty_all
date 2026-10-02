using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using dEngage.Loyalty.Api.Events;
using dEngage.Loyalty.Api.Framework.Auth;
using dEngage.Loyalty.Api.Framework.Json;
using dEngage.Loyalty.Api.Programs;
using dEngage.Loyalty.Api.Rewards;
using dEngage.Loyalty.IntegrationTests.Fixtures;
using dEngage.Loyalty.RuleEngine.Campaigns.Streak;
using dEngage.Loyalty.Schema.Entities;
using dEngage.Loyalty.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AccountTypeEntity = dEngage.Loyalty.Schema.Entities.AccountType;
using ProgramEntity = dEngage.Loyalty.Schema.Entities.Program;

namespace dEngage.Loyalty.IntegrationTests.Api;

// CR 2026-09-30 P2–P4 through the real Nancy pipeline: the Acquisition × RewardType matrix and
// retired values (A1), cashback wallet/currency checks (A3), second-admin approval (A4), program
// slug + prefixed reward names (A5), streak reward eligibility and the in-use guards (§3.7),
// tier ownership and the tier-in-use guard (§3.6), and tenant isolation.
public sealed class RewardCatalogCr0930ApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string TenantSlug = "cr0930-rewards";
    private const string OtherTenantSlug = "cr0930-rewards-b";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _adminA;   // creates rewards
    private readonly HttpClient _adminB;   // a different admin, approves them
    private readonly HttpClient _otherTenantAdmin;

    private Guid _programId;
    private string _slug = default!;
    private Guid _pointsId;
    private Guid _sarWalletId;
    private Guid _usdWalletId;
    private Guid _goldTierId;
    private Guid _otherProgramId;
    private Guid _otherProgramWalletId;
    private Guid _otherProgramTierId;
    private Guid _otherTenantWalletId;

    public RewardCatalogCr0930ApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _adminA = factory.CreateClient();
        _adminB = factory.CreateClient();
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

            // Unique per test: the class fixture shares one database across tests.
            _slug = $"shop{Guid.NewGuid():N}"[..14];
            _programId = Guid.NewGuid();
            _otherProgramId = Guid.NewGuid();
            var otherTenantProgramId = Guid.NewGuid();
            db.Programs.AddRange(
                new ProgramEntity { Id = _programId, TenantId = tenant.Id, Name = "Shop", Slug = _slug, Status = ProgramStatus.Inactive, CreatedAt = DateTime.UtcNow },
                new ProgramEntity { Id = _otherProgramId, TenantId = tenant.Id, Name = "Other", Status = ProgramStatus.Inactive, CreatedAt = DateTime.UtcNow },
                new ProgramEntity { Id = otherTenantProgramId, TenantId = otherTenant.Id, Name = "B", Status = ProgramStatus.Inactive, CreatedAt = DateTime.UtcNow });

            _pointsId = Guid.NewGuid();
            _sarWalletId = Guid.NewGuid();
            _usdWalletId = Guid.NewGuid();
            _otherProgramWalletId = Guid.NewGuid();
            _otherTenantWalletId = Guid.NewGuid();
            db.AccountTypes.AddRange(
                Account(_pointsId, tenant.Id, _programId, "POINTS", """{"decimals": 0}""", tierQualifying: true),
                Account(_sarWalletId, tenant.Id, _programId, "CASH", """{"currency": "SAR", "decimals": 2}"""),
                Account(_usdWalletId, tenant.Id, _programId, "CASH", """{"currency": "USD", "decimals": 2}"""),
                Account(_otherProgramWalletId, tenant.Id, _otherProgramId, "CASH", """{"currency": "SAR", "decimals": 2}"""),
                Account(_otherTenantWalletId, otherTenant.Id, otherTenantProgramId, "CASH", """{"currency": "SAR", "decimals": 2}"""));

            _goldTierId = Guid.NewGuid();
            _otherProgramTierId = Guid.NewGuid();
            db.TierDefinitions.AddRange(
                new TierDefinition { Id = _goldTierId, TenantId = tenant.Id, ProgramId = _programId, Name = "gold", DisplayName = "Gold", MinPoints = 1000, SortOrder = 2, CreatedAt = DateTime.UtcNow },
                new TierDefinition { Id = _otherProgramTierId, TenantId = tenant.Id, ProgramId = _otherProgramId, Name = "gold", DisplayName = "Gold", MinPoints = 1000, SortOrder = 2, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();
        _adminA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt, "admin-a@test.local"));
        _adminB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssuePlatformAdminToken(jwt, "admin-b@test.local"));
        _otherTenantAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtIssuingHelper.IssueTenantAdminToken(jwt, otherTenant));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Tenant Ensure(dEngage.Loyalty.Schema.LoyaltyDbContext db, string slug)
    {
        var tenant = db.Tenants.SingleOrDefault(t => t.Slug == slug);
        if (tenant is not null) return tenant;
        tenant = new Tenant { Id = Guid.NewGuid(), Slug = slug, Name = slug, Status = TenantStatus.Active, CreatedAt = DateTime.UtcNow };
        db.Tenants.Add(tenant);
        return tenant;
    }

    private static AccountTypeEntity Account(Guid id, Guid tenantId, Guid programId, string type, string config, bool tierQualifying = false) =>
        new() { Id = id, TenantId = tenantId, ProgramId = programId, Type = type, Name = type, Config = config, IsTierQualifying = tierQualifying, CreatedAt = DateTime.UtcNow };

    private string ProgramUrl => $"/api/v1/tenants/{TenantSlug}/programs/{_programId}";
    private string RewardsUrl => $"{ProgramUrl}/rewards";

    private static StringContent Json(object body) => new(
        System.Text.Json.JsonSerializer.Serialize(body, JsonConventions.Options), System.Text.Encoding.UTF8, "application/json");

    private string Name(string suffix) => $"{_slug}_{suffix}";

    private object Cashback(string name, string acquisition = RewardAcquisition.PointsPurchase,
        string currency = "SAR", Guid? wallet = null, string amount = "10.00") => new
    {
        name,
        displayName = name,
        acquisition,
        rewardType = RewardType.Cashback,
        pointsPrice = acquisition == RewardAcquisition.PointsPurchase ? "100" : null,
        pointsAccountTypeId = acquisition == RewardAcquisition.PointsPurchase ? _pointsId : (Guid?)null,
        typeConfig = new Dictionary<string, object?> { ["amount"] = amount, ["currency"] = currency, ["cash_account_type_id"] = (wallet ?? _sarWalletId).ToString() },
        isActive = true
    };

    private object TierUpgrade(string name, string acquisition = RewardAcquisition.StreakCompletion, Guid? tier = null) => new
    {
        name,
        displayName = name,
        acquisition,
        rewardType = RewardType.TierUpgrade,
        pointsPrice = acquisition == RewardAcquisition.PointsPurchase ? "100" : null,
        pointsAccountTypeId = acquisition == RewardAcquisition.PointsPurchase ? _pointsId : (Guid?)null,
        typeConfig = new Dictionary<string, object?> { ["target_tier_id"] = (tier ?? _goldTierId).ToString(), ["duration_days"] = 30 },
        isActive = true
    };

    private async Task<RewardResponse> CreateAsync(object body, HttpClient? client = null)
    {
        var response = await (client ?? _adminA).PostAsync(RewardsUrl, Json(body));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RewardResponse>(JsonConventions.Options))!;
    }

    private async Task<HttpStatusCode> StatusOfCreate(object body) => (await _adminA.PostAsync(RewardsUrl, Json(body))).StatusCode;

    private Task<HttpResponseMessage> Approve(HttpClient client, Guid rewardId) =>
        client.PatchAsync($"{RewardsUrl}/{rewardId}/approve", Json(new { }));

    // ── A1 + matrix ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cashback_can_be_bought_with_points_or_earned_through_a_streak()
    {
        (await CreateAsync(Cashback(Name("buy")))).Acquisition.Should().Be(RewardAcquisition.PointsPurchase);
        (await CreateAsync(Cashback(Name("streak"), RewardAcquisition.StreakCompletion))).Acquisition.Should().Be(RewardAcquisition.StreakCompletion);
    }

    [Fact]
    public async Task A_tier_upgrade_can_only_be_earned_through_a_streak()
    {
        (await StatusOfCreate(TierUpgrade(Name("bought_tier"), RewardAcquisition.PointsPurchase))).Should().Be(HttpStatusCode.BadRequest);
        (await CreateAsync(TierUpgrade(Name("streak_tier")))).Status.Should().Be(RewardStatus.Active);
    }

    [Theory]
    [InlineData(RewardType.Discount)]
    [InlineData(RewardType.PointsBonus)]
    [InlineData(RewardType.FreeProduct)]
    [InlineData(RewardType.GiftCard)]
    public async Task Retired_reward_types_cannot_be_created(string retiredType)
    {
        var body = new
        {
            name = Name("old"), displayName = "old", acquisition = RewardAcquisition.PointsPurchase, rewardType = retiredType,
            pointsPrice = "100", pointsAccountTypeId = _pointsId, typeConfig = new { }, isActive = true
        };
        (await StatusOfCreate(body)).Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stamp_completion_is_retired()
    {
        (await StatusOfCreate(Cashback(Name("stamp"), RewardAcquisition.StampCompletion))).Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_retired_reward_cannot_be_edited_or_reactivated()
    {
        var retiredId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            var program = db.Programs.Single(p => p.Id == _programId);
            db.RewardDefinitions.Add(new RewardDefinition
            {
                Id = retiredId, TenantId = program.TenantId, ProgramId = _programId, Name = Name("legacy"), DisplayName = "Legacy",
                Acquisition = RewardAcquisition.PointsPurchase, RewardType = RewardType.Discount, TypeConfig = "{}",
                IsActive = false, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        (await _adminA.PatchAsync($"{RewardsUrl}/{retiredId}", Json(new { displayName = "x" }))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _adminA.PatchAsync($"{RewardsUrl}/{retiredId}/active", Json(new { isActive = true }))).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── A3: cashback currency and wallet ─────────────────────────────────────────────────────

    [Fact]
    public async Task Cashback_currency_must_be_on_the_supported_list()
    {
        (await StatusOfCreate(Cashback(Name("xyz"), currency: "XYZ"))).Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cashback_currency_must_match_the_selected_wallet()
    {
        (await StatusOfCreate(Cashback(Name("mismatch"), currency: "SAR", wallet: _usdWalletId))).Should().Be(HttpStatusCode.BadRequest);
        (await CreateAsync(Cashback(Name("usd"), currency: "USD", wallet: _usdWalletId))).Should().NotBeNull();
    }

    [Fact]
    public async Task Cashback_wallet_must_belong_to_this_program()
    {
        (await StatusOfCreate(Cashback(Name("foreign"), wallet: _otherProgramWalletId))).Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cashback_wallet_from_another_tenant_is_rejected()
    {
        (await StatusOfCreate(Cashback(Name("cross_tenant"), wallet: _otherTenantWalletId))).Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cashback_amount_cannot_have_more_decimals_than_the_wallet()
    {
        (await StatusOfCreate(Cashback(Name("precise"), amount: "10.125"))).Should().Be(HttpStatusCode.BadRequest);
    }

    // ── A4: approval ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_cashback_reward_is_pending_and_its_creator_cannot_approve_it()
    {
        var reward = await CreateAsync(Cashback(Name("approve")));
        reward.Status.Should().Be(RewardStatus.PendingApproval);
        reward.CreatedBy.Should().NotBeNullOrEmpty();

        (await Approve(_adminA, reward.Id)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var approved = await Approve(_adminB, reward.Id);
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        (await approved.Content.ReadFromJsonAsync<RewardResponse>(JsonConventions.Options))!.Status.Should().Be(RewardStatus.Active);
    }

    [Fact]
    public async Task Changing_what_an_approved_cashback_pays_sends_it_back_for_approval_and_its_currency_is_locked()
    {
        var reward = await CreateAsync(Cashback(Name("reapprove")));
        (await Approve(_adminB, reward.Id)).EnsureSuccessStatusCode();

        var changed = await _adminA.PatchAsync($"{RewardsUrl}/{reward.Id}", Json(new
        {
            typeConfig = new Dictionary<string, object?> { ["amount"] = "20.00", ["currency"] = "SAR", ["cash_account_type_id"] = _sarWalletId.ToString() }
        }));
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        (await changed.Content.ReadFromJsonAsync<RewardResponse>(JsonConventions.Options))!.Status.Should().Be(RewardStatus.PendingApproval);

        var currency = await _adminA.PatchAsync($"{RewardsUrl}/{reward.Id}", Json(new
        {
            typeConfig = new Dictionary<string, object?> { ["amount"] = "20.00", ["currency"] = "USD", ["cash_account_type_id"] = _usdWalletId.ToString() }
        }));
        currency.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_admin_of_another_tenant_cannot_approve_this_tenants_reward()
    {
        var reward = await CreateAsync(Cashback(Name("tenancy")));

        var response = await Approve(_otherTenantAdmin, reward.Id);

        ((int)response.StatusCode).Should().BeOneOf(403, 404);
    }

    // ── A5: program slug and prefixed names ──────────────────────────────────────────────────

    [Fact]
    public async Task A_reward_name_must_start_with_the_program_slug()
    {
        (await StatusOfCreate(Cashback("cash_10"))).Should().Be(HttpStatusCode.BadRequest);
        (await StatusOfCreate(Cashback("otherslug_cash_10"))).Should().Be(HttpStatusCode.BadRequest);
        (await CreateAsync(Cashback(Name("cash_10")))).Name.Should().Be($"{_slug}_cash_10");
    }

    [Fact]
    public async Task An_active_reward_name_cannot_be_reused()
    {
        await CreateAsync(Cashback(Name("dup")));
        (await StatusOfCreate(Cashback(Name("dup")))).Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_new_program_gets_a_slug_derived_from_its_name()
    {
        var name = $"Fin Tech {Guid.NewGuid():N}"[..14];
        var response = await _adminA.PostAsync($"/api/v1/tenants/{TenantSlug}/programs", Json(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var program = (await response.Content.ReadFromJsonAsync<ProgramResponse>(JsonConventions.Options))!;
        program.Slug.Should().MatchRegex("^[a-z0-9]+(-[a-z0-9]+)*$").And.StartWith("fin-tech-");
    }

    [Theory]
    [InlineData("has_underscore")]
    [InlineData("UPPER")]
    [InlineData("x")]
    public async Task An_invalid_program_slug_is_rejected(string slug)
    {
        var response = await _adminA.PostAsync($"/api/v1/tenants/{TenantSlug}/programs", Json(new { name = "Bad slug", slug }));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_program_slug_must_be_unique_and_is_locked_once_published()
    {
        var taken = await _adminA.PostAsync($"/api/v1/tenants/{TenantSlug}/programs", Json(new { name = "Copy", slug = _slug }));
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var db = _factory.CreateDbContext())
        {
            db.Programs.Single(p => p.Id == _programId).PublicationStatus = ProgramPublicationStatus.Published;
            await db.SaveChangesAsync();
        }
        var locked = await _adminA.PatchAsync(ProgramUrl, Json(new { slug = "renamed" }));
        locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── §3.6 tier ownership / tier in use ────────────────────────────────────────────────────

    [Fact]
    public async Task A_tier_upgrade_must_target_a_tier_of_this_program()
    {
        (await StatusOfCreate(TierUpgrade(Name("foreign_tier"), tier: _otherProgramTierId))).Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_tier_targeted_by_an_active_reward_cannot_be_deleted()
    {
        await CreateAsync(TierUpgrade(Name("keeps_gold")));

        var response = await _adminA.DeleteAsync($"{ProgramUrl}/tiers/{_goldTierId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── §3.7 streak eligibility and in-use guard ─────────────────────────────────────────────

    private object Streak(Guid rewardId) => new
    {
        name = $"streak-{Guid.NewGuid():N}"[..20],
        trigger = "order.created",
        targetAccountTypeId = _pointsId,
        config = new StreakConfig
        {
            Period = StreakPeriod.Day, WeekStart = StreakWeekStart.Monday, TargetPeriods = 3,
            Aggregate = new StreakAggregate { Metric = StreakMetric.Count, Threshold = 1 },
            Timezone = "UTC", OnComplete = StreakOnComplete.Restart,
            Reward = new StreakReward { Kind = StreakRewardKind.RewardDefinition, RewardDefinitionId = rewardId }
        }
    };

    [Fact]
    public async Task A_streak_cannot_pay_a_reward_that_is_pending_or_bought_with_points()
    {
        var pending = await CreateAsync(Cashback(Name("streak_pending"), RewardAcquisition.StreakCompletion));
        var purchasable = await CreateAsync(Cashback(Name("for_sale")));
        (await Approve(_adminB, purchasable.Id)).EnsureSuccessStatusCode();

        (await _adminA.PostAsync($"{ProgramUrl}/streak-campaigns", Json(Streak(pending.Id)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _adminA.PostAsync($"{ProgramUrl}/streak-campaigns", Json(Streak(purchasable.Id)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_reward_paid_by_an_active_streak_cannot_be_deactivated()
    {
        var reward = await CreateAsync(TierUpgrade(Name("streak_gold")));
        var created = await _adminA.PostAsync($"{ProgramUrl}/streak-campaigns", Json(Streak(reward.Id)));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var response = await _adminA.PatchAsync($"{RewardsUrl}/{reward.Id}/active", Json(new { isActive = false }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── §3.7 reward.purchase by id ───────────────────────────────────────────────────────────

    [Fact]
    public void A_reward_purchase_needs_a_reward_name_or_a_reward_id()
    {
        var validator = new RewardPurchaseRequestValidator();

        validator.Validate(new RewardPurchaseRequest("c1", null, null)).IsValid.Should().BeFalse();
        validator.Validate(new RewardPurchaseRequest("c1", "shop_cash_10", null)).IsValid.Should().BeTrue();
        validator.Validate(new RewardPurchaseRequest("c1", null, null, Guid.NewGuid())).IsValid.Should().BeTrue();
    }
}
